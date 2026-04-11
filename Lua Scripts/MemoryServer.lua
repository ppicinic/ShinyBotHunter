luanet.load_assembly("System")

local TcpListener = luanet.import_type("System.Net.Sockets.TcpListener")
local IPAddress   = luanet.import_type("System.Net.IPAddress")

local port = tonumber(os.getenv("BIZHAWK_MEMORY_PORT")) or 50001

local server = TcpListener(IPAddress.Loopback, port)
local client = nil
local stream = nil

server:Start()
print("Memory server waiting on port " .. port .. "...")

local function disconnect()
    pcall(function() stream:Close() end)
    pcall(function() client:Close() end)
    stream = nil
    client = nil
    print("Memory client disconnected, waiting for new connection...")
end

local function read_exact(n)
    local bytes = {}
    for i = 1, n do
        local b = stream:ReadByte()
        if b == -1 then
            disconnect()
            return nil
        end
        bytes[i] = b
    end
    return bytes
end

local function u32_le(bytes, i)
    return bytes[i] | (bytes[i+1] << 8) | (bytes[i+2] << 16) | (bytes[i+3] << 24)
end

local function handle_request()
    local header = read_exact(9)
    if not header then return end

    local cmd  = header[1]
    local addr = u32_le(header, 2)
    local aux  = u32_le(header, 6)

    if cmd == 0xFF then
        disconnect()
    elseif cmd == 1 then
        for i = 0, aux - 1 do
            stream:WriteByte(mainmemory.read_u8(addr + i))
        end
    elseif cmd == 2 then
        mainmemory.write_u8(addr, aux & 0xFF)
        stream:WriteByte(1)
    end
end

event.onframestart(function()
    if client == nil and server:Pending() then
        client = server:AcceptTcpClient()
        stream  = client:GetStream()
        print("Memory client connected.")
    end

    if stream == nil then return end

    if stream.DataAvailable then
        handle_request()
    end
end)