luanet.load_assembly("System")

local TcpListener  = luanet.import_type("System.Net.Sockets.TcpListener")
local IPAddress    = luanet.import_type("System.Net.IPAddress")
local StreamReader = luanet.import_type("System.IO.StreamReader")
local StreamWriter = luanet.import_type("System.IO.StreamWriter")

local DEBUG = true
local port = tonumber(os.getenv("BIZHAWK_COMMAND_PORT")) or 50000

local server = TcpListener(IPAddress.Loopback, port)
local serverClient, stream, reader, writer = nil, nil, nil, nil

local held_frames   = {} 
local command_stack = {}

local function log(msg)
    if DEBUG then print(msg) end
end

server:Start()
print("Command server waiting on port " .. port .. "...")

local function disconnect()
    pcall(function() stream:Close() end)
    pcall(function() serverClient:Close() end)
    stream, serverClient, reader, writer = nil, nil, nil, nil
    held_frames   = {}
    command_stack = {}
    print("Command client disconnected, waiting for new connection...")
end

local function sendResponse(msg)
    if writer then
        pcall(function() writer:WriteLine(msg) end)
    end
end

-- ── Stack-based macro executor ────────────────────────────────────────────────

local function push(cmd)
    table.insert(command_stack, cmd)
end

local function pop()
    return table.remove(command_stack)
end

local function parse_frame_commands(macro_str)
    local frames = {}
    for frame_str in macro_str:gmatch("[^;]+") do
        -- 'started' tracks if we've logged/initialized this frame yet
        local frame_obj = { action = "FRAME", buttons = {}, wait = 0, started = false }
        
        for cmd_str in frame_str:gmatch("[^:]+") do
            local parts = {}
            for p in cmd_str:gmatch("[^%.]+") do parts[#parts+1] = p end

            local action = parts[1]
            if action == "W" then
                frame_obj.wait = tonumber(parts[2]) or tonumber(table.concat(parts, ".", 2)) or 1
            else
                local button = parts[2] or ""
                local num    = tonumber(parts[3]) or 1
                
                if action == "H" and num == 1 then
                    held_frames[button] = true
                elseif action == "R" then
                    held_frames[button] = nil
                else
                    table.insert(frame_obj.buttons, { name = button, framesLeft = num })
                end
            end
        end
        frames[#frames+1] = frame_obj
    end
    return frames
end

local function push_macro_string(macro_str, is_repeat)
    local frames = parse_frame_commands(macro_str)

    if is_repeat then
        push({ action="RP", macro_str=macro_str })
    else
        push({ action="MACRO_END" })
    end

    for i = #frames, 1, -1 do
        push(frames[i])
    end
end

local function step_stack()
    if #command_stack == 0 then return end

    local top = command_stack[#command_stack]

    if top.action == "FRAME" then
        -- LOGGING: Trigger only the first time this frame is processed
        if not top.started then
            local desc = {}
            for _, b in ipairs(top.buttons) do 
                table.insert(desc, string.format("%s(%d)", b.name, b.framesLeft)) 
            end
            if top.wait > 0 then table.insert(desc, "Wait("..top.wait..")") end
            log("FRAME START: " .. table.concat(desc, ", "))
            top.started = true
        end

        local still_active = false

        if top.wait > 0 then
            top.wait = top.wait - 1
            if top.wait > 0 then still_active = true end
        end

        for _, btn in ipairs(top.buttons) do
            if btn.framesLeft > 0 then
                btn.framesLeft = btn.framesLeft - 1
                if btn.framesLeft > 0 then still_active = true end
            end
        end

        if not still_active then
            pop()
            log("FRAME COMPLETE")
        end

    elseif top.action == "RP" then
        pop()
        log("REPEAT macro started")
        push_macro_string(top.macro_str, true)

    elseif top.action == "MACRO_END" then
        pop()
        log("MACRO END")
        sendResponse("macro_done")
    end
end

-- ── Command handler ───────────────────────────────────────────────────────────

local function handle_request(msg)
    if not msg then return end
    if msg == "disconnect" then disconnect() return end

    local parts = {}
    for p in msg:gmatch("[^_]+") do parts[#parts+1] = p end

    if parts[1] == "hold" then
        local button = parts[2]
        local frames = tonumber(parts[3])

        if frames and frames > 0 then
            -- Wrap in a FRAME object so step_stack can process it
            push({
                action = "FRAME",
                buttons = {{ name = button, framesLeft = frames }},
                wait = 0,
                started = false
            })
            log("CMD STACK HOLD: " .. button .. " for " .. frames .. " frames")
        else
            -- No frame count or count is 1, treat as indefinite
            held_frames[button] = true
            log("CMD HOLD INDEFINITE: " .. button)
        end
    elseif msg == "release" then
        held_frames = {}
        log("CMD RELEASE ALL")
    elseif parts[1] == "release" then
        held_frames[parts[2]] = nil
        log("CMD RELEASE " .. parts[2])
    elseif parts[1] == "macro" and parts[2] == "string" then
        local macro_str = msg:sub(14)
        log("CMD MACRO RECEIVED: " .. macro_str) -- Restored log
        push_macro_string(macro_str, false)
    elseif parts[1] == "RP" then
        local macro_str = msg:sub(4)
        log("CMD REPEAT MACRO RECEIVED: " .. macro_str) -- Restored log
        push_macro_string(macro_str, true)
    elseif msg == "macro_clear" then
        command_stack = {}
        held_frames   = {}
        log("CMD MACRO CLEAR")
        sendResponse("macro_done")
    elseif msg == "flush" then
    	client.saveram()
    	log("CMD FLUSH: SaveRAM written to disk")
    	sendResponse("flushed")
    elseif msg == "ping" then
        sendResponse("pong")
    end
end

-- ── Frame loop ────────────────────────────────────────────────────────────────

event.onframestart(function()
    if serverClient == nil and server:Pending() then
        serverClient = server:AcceptTcpClient()
        stream = serverClient:GetStream()
        reader = StreamReader(stream)
        writer = StreamWriter(stream)
        writer.AutoFlush = true
        stream.ReadTimeout = 1
        print("Command client connected.")
    end

    if stream ~= nil then
        local ok, msg = pcall(function() return reader:ReadLine() end)
        if ok and msg then handle_request(msg) end
    end

    local top = command_stack[#command_stack]
    local buttons_to_press = {}

    for btn, _ in pairs(held_frames) do
        buttons_to_press[btn] = true
    end

    if top and top.action == "FRAME" then
        for _, btn in ipairs(top.buttons) do
            if btn.framesLeft > 0 then
                buttons_to_press[btn.name] = true
            end
        end
    end

    joypad.set(buttons_to_press)
    step_stack()
end)