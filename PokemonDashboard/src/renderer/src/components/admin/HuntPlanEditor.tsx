import React, { useEffect, useRef, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { HuntPlan, PickerMilestone, PlanRule, PlanTime, Weekday } from '../../types/api'
import { planPreview, WEEKDAYS } from './actionSchema'

// Plans are HGSS only by decision; the server offers this field only where the profile has HasHuntPlans.
const PLAN_GAME = 'HGSS'

const TIMES: { time: PlanTime; hours: string }[] = [
  { time: 'Morning', hours: '4:00-10:00' },
  { time: 'Day', hours: '10:00-20:00' },
  { time: 'Night', hours: '20:00-4:00' }
]

// The preview's fills, from the theme's neutral and accent ramps: the default first, then each other milestone of the plan.
const FILLS = ['bg-slate-700 text-slate-200', 'bg-indigo-700 text-white', 'bg-indigo-500 text-white', 'bg-slate-500 text-white', 'bg-slate-300 text-slate-900', 'bg-indigo-600/50 text-white']

const SELECT = 'bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500'
const BUTTON = 'text-xs text-slate-400 hover:text-white px-1.5 py-0.5 rounded border border-slate-600 disabled:opacity-40'

interface Draft {
  name: string
  default: string
  rules: PlanRule[]
}

const EMPTY: Draft = { name: '', default: '', rules: [] }

const hh = (hour: number): string => `${String(hour).padStart(2, '0')}:00`

// Start refuses a plan naming one of these (FR-35); a milestone this file does not list is refused there too, by name.
function refusal(id: string, milestones: PickerMilestone[]): string | null {
  const m = milestones.find((x) => x.name === id)
  if (!m) return null
  if (!m.huntable) return 'no hunt yet: Start refuses the plan'
  if (!m.anchor) return 'no anchor on this file: Start refuses the plan'
  return null
}

function MilestoneSelect({ value, onChange, milestones }: { value: string; onChange: (id: string) => void; milestones: PickerMilestone[] }): JSX.Element {
  const hint = refusal(value, milestones)
  return (
    <>
      <select value={value} onChange={(e) => onChange(e.target.value)} className={`flex-1 min-w-0 ${SELECT}`}>
        <option value="">Select a milestone...</option>
        {value && !milestones.some((m) => m.name === value) && <option value={value}>{value}</option>}
        {milestones.map((m) => (
          <option key={m.name} value={m.name}>{m.name}</option>
        ))}
      </select>
      {hint && <span className="text-xs text-amber-400">{hint}</span>}
    </>
  )
}

function Chip({ on, onClick, title, children }: { on: boolean; onClick: () => void; title?: string; children: React.ReactNode }): JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      title={title}
      aria-pressed={on}
      className={`text-xs px-2 py-0.5 rounded-full border transition-colors ${on ? 'bg-indigo-700 text-white border-indigo-500' : 'border-slate-600 text-slate-400 hover:text-white'}`}
    >
      {children}
    </button>
  )
}

/**
 * Reads the saved plans and shows them, or the error; a response to a read made before a later one (or before the unmount,
 * which bumps `seq` too) is dropped, so a re-read on focus cannot be overwritten by an older list.
 */
export async function loadPlans(
  read: () => Promise<Response>,
  seq: { current: number },
  show: (list: HuntPlan[] | null, error: string | null) => void
): Promise<void> {
  const mine = ++seq.current
  try {
    const r = await read()
    if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
    const list = await r.json() as HuntPlan[]
    if (mine === seq.current) show(list, null)
  } catch (err) {
    if (mine === seq.current) show(null, err instanceof Error ? err.message : 'Failed to load the plans')
  }
}

/**
 * The Hunt Plan field of Wild Encounter (FR-37a) and, under Edit plans, the saved plans' editor (FR-37, FR-37b). Nothing is
 * written until Save, Rename or Delete; while the draft differs from what was loaded, whatever would drop it is greyed and
 * onDirtyChange tells the card to grey its own.
 */
export function HuntPlanEditor({
  value,
  onChange,
  milestones,
  onDirtyChange
}: {
  value: string
  onChange: (plan: string) => void
  // The bot's file's milestones, for names and anchors.
  milestones: PickerMilestone[]
  onDirtyChange: (dirty: boolean) => void
}): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''
  const [plans, setPlans] = useState<HuntPlan[]>([])
  const [listError, setListError] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  // The saved plan the draft edits, by its saved name, or null for a new plan.
  const [editing, setEditing] = useState<string | null>(null)
  const [draft, setDraft] = useState<Draft | null>(null)
  // The draft as last loaded or saved, to tell unsaved changes.
  const [baseline, setBaseline] = useState<Draft>(EMPTY)
  const [day, setDay] = useState<Weekday>('Sunday')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  // The number of the latest list load; a response to an earlier one, or one after unmount, is dropped. Written only by load
  // and the unmount below, on the renderer's one thread.
  const loadSeq = useRef(0)

  // Also on the select's focus and on opening the editor: another card may have saved, renamed or deleted a plan since.
  const load = (): Promise<void> => loadPlans(() => api.getPlans(apiUrl, PLAN_GAME), loadSeq, (list, err) => {
    if (list) setPlans(list)
    setListError(err)
  })

  useEffect(() => {
    if (apiUrl) void load()
    return () => {
      loadSeq.current++
    }
  }, [apiUrl])

  const contentDirty = draft !== null && JSON.stringify([draft.default, draft.rules]) !== JSON.stringify([baseline.default, baseline.rules])
  const dirty = contentDirty || (draft !== null && draft.name !== baseline.name)
  useEffect(() => {
    onDirtyChange(dirty)
  }, [dirty])
  useEffect(() => () => onDirtyChange(false), [])

  const edit = (plan: HuntPlan | null): void => {
    const d = plan ? { name: plan.name, default: plan.default, rules: plan.rules } : EMPTY
    setEditing(plan?.name ?? null)
    setDraft(d)
    setBaseline(d)
    setError(null)
    setConfirmingDelete(false)
  }

  const send = async (call: () => Promise<Response>, then: () => void): Promise<void> => {
    setBusy(true)
    setError(null)
    try {
      const r = await call()
      if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
      then()
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The request failed')
    } finally {
      setBusy(false)
    }
  }

  // A saved plan is saved under its saved name; a name typed in its box waits for Rename.
  const save = (d: Draft): Promise<void> => {
    const name = editing ?? d.name.trim()
    return send(() => api.savePlan(apiUrl, PLAN_GAME, name, { game: PLAN_GAME, name, default: d.default, rules: d.rules }), () => {
      if (editing === null) setDraft((now) => (now === d ? { ...d, name } : now))
      setEditing(name)
      setBaseline({ name, default: d.default, rules: d.rules })
    })
  }

  const rename = (from: string, to: string): Promise<void> => send(() => api.renamePlan(apiUrl, PLAN_GAME, from, to), () => {
    setEditing(to)
    setBaseline((b) => ({ ...b, name: to }))
    setDraft((now) => (now ? { ...now, name: to } : now))
    if (value === from) onChange(to)
  })

  const remove = (name: string): Promise<void> => send(() => api.deletePlan(apiUrl, PLAN_GAME, name), () => {
    if (value === name) onChange('')
    setEditing(null)
    setDraft(null)
    setBaseline(EMPTY)
    setConfirmingDelete(false)
  })

  const setRule = (index: number, rule: PlanRule): void => {
    if (draft) setDraft({ ...draft, rules: draft.rules.map((r, i) => (i === index ? rule : r)) })
  }
  const moveRule = (index: number, by: number): void => {
    if (!draft) return
    const rules = [...draft.rules]
    ;[rules[index], rules[index + by]] = [rules[index + by], rules[index]]
    setDraft({ ...draft, rules })
  }

  const nameOf = (id: string): string => milestones.find((m) => m.name === id)?.name ?? id
  const planMilestones = draft ? Array.from(new Set([draft.default, ...draft.rules.map((r) => r.milestone)])) : []
  const fill = (id: string): string => FILLS[planMilestones.indexOf(id) % FILLS.length]
  const runs = draft ? planPreview(draft, day) : []
  const shown = Array.from(new Set(runs.map((r) => r.milestone)))
  const newName = draft?.name.trim() ?? ''
  const nameTaken = plans.some((p) => p.name === newName)

  return (
    <div className="flex-1 space-y-1">
      <div className="flex items-center gap-2">
        <select value={value} onChange={(e) => onChange(e.target.value)} onFocus={() => void load()} className={`flex-1 ${SELECT}`}>
          <option value="">No plan</option>
          {value && !plans.some((p) => p.name === value) && <option value={value}>{value}</option>}
          {plans.map((p) => (
            <option key={p.name} value={p.name}>{p.name}</option>
          ))}
        </select>
        <button
          type="button"
          onClick={() => {
            if (!open) {
              edit(plans.find((p) => p.name === value) ?? null)
              void load()
            } else setDraft(null)
            setOpen(!open)
          }}
          disabled={open && dirty}
          title={open && dirty ? 'Save or discard the changes first' : undefined}
          className={BUTTON}
        >
          {open ? 'Close plans' : 'Edit plans'}
        </button>
      </div>
      {listError && <div className="text-xs text-red-400">{listError}</div>}
      {open && (
        <div className="border border-slate-700/60 rounded px-2 py-1.5 space-y-1.5">
          <div className="flex items-center gap-1 flex-wrap">
            {plans.map((p) => (
              <button
                key={p.name}
                type="button"
                onClick={() => edit(p)}
                disabled={dirty || busy}
                className={`px-2 py-0.5 text-xs rounded transition-colors disabled:opacity-50 ${editing === p.name ? 'bg-indigo-700 text-white' : 'bg-card-border text-slate-400 hover:text-white'}`}
              >
                {p.name}
              </button>
            ))}
            <button type="button" onClick={() => edit(null)} disabled={dirty || busy} className={BUTTON}>+ New plan</button>
          </div>
          {draft && (
            <>
              <div className="flex items-center gap-2">
                <span className="text-xs text-slate-500 w-24 flex-shrink-0">Name</span>
                <input
                  type="text"
                  value={draft.name}
                  onChange={(e) => setDraft({ ...draft, name: e.target.value })}
                  placeholder="Plan name"
                  className={`flex-1 placeholder-slate-500 ${SELECT}`}
                />
                {editing !== null && (
                  <button
                    type="button"
                    onClick={() => rename(editing, newName)}
                    disabled={busy || newName === '' || newName === editing}
                    className={BUTTON}
                  >
                    Rename
                  </button>
                )}
                {editing === null && nameTaken && <span className="text-xs text-amber-400">already a plan</span>}
              </div>
              <div className="flex items-center gap-2">
                <span className="text-xs text-slate-500 w-24 flex-shrink-0">Otherwise hunt</span>
                <MilestoneSelect value={draft.default} onChange={(id) => setDraft({ ...draft, default: id })} milestones={milestones} />
              </div>
              <div className="text-xs text-slate-400">Rules, first match wins</div>
              {draft.rules.map((rule, i) => (
                <div key={i} className="flex gap-2 border-t border-slate-700/50 pt-1">
                  <div className="flex flex-col items-center gap-0.5">
                    <span className="text-xs text-slate-500">{i + 1}</span>
                    <button type="button" onClick={() => moveRule(i, -1)} disabled={i === 0} className={BUTTON}>Up</button>
                    <button type="button" onClick={() => moveRule(i, 1)} disabled={i === draft.rules.length - 1} className={BUTTON}>Down</button>
                  </div>
                  <div className="flex-1 min-w-0 space-y-1">
                    <div className="flex items-center gap-1 flex-wrap">
                      <span className="text-xs text-slate-500 w-12 flex-shrink-0">When</span>
                      {TIMES.map(({ time, hours }) => (
                        <Chip
                          key={time}
                          on={rule.times.includes(time)}
                          title={hours}
                          onClick={() => setRule(i, { ...rule, times: TIMES.map((t) => t.time).filter((t) => (t === time) !== rule.times.includes(t)) })}
                        >
                          {time}
                        </Chip>
                      ))}
                      {rule.times.length === 0 && <span className="text-xs text-slate-500">none on: any time</span>}
                    </div>
                    <div className="flex items-center gap-1 flex-wrap">
                      <span className="text-xs text-slate-500 w-12 flex-shrink-0">Days</span>
                      {WEEKDAYS.map((weekday) => (
                        <Chip
                          key={weekday}
                          on={rule.weekdays.includes(weekday)}
                          title={weekday}
                          onClick={() => setRule(i, { ...rule, weekdays: WEEKDAYS.filter((d) => (d === weekday) !== rule.weekdays.includes(d)) })}
                        >
                          {weekday.slice(0, 2)}
                        </Chip>
                      ))}
                      {rule.weekdays.length === 0 && <span className="text-xs text-slate-500">none on: any day</span>}
                    </div>
                    <div className="flex items-center gap-1 flex-wrap">
                      <span className="text-xs text-slate-500 w-12 flex-shrink-0">Go to</span>
                      <MilestoneSelect value={rule.milestone} onChange={(id) => setRule(i, { ...rule, milestone: id })} milestones={milestones} />
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => setDraft({ ...draft, rules: draft.rules.filter((_, j) => j !== i) })}
                    aria-label={`Remove rule ${i + 1}`}
                    className="self-start text-xs text-red-400 hover:text-red-300 transition-colors"
                  >
                    Remove
                  </button>
                </div>
              ))}
              <div className="border-t border-slate-700/50 pt-1">
                <button
                  type="button"
                  onClick={() => setDraft({ ...draft, rules: [...draft.rules, { times: [], weekdays: [], milestone: '' }] })}
                  className={BUTTON}
                >
                  Add rule
                </button>
              </div>
              <div className="text-xs text-slate-500">
                Times are the game&apos;s own clock: morning 4:00-10:00, day 10:00-20:00, night 20:00-4:00.
              </div>
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <span className="text-xs text-slate-400">Preview</span>
                  <select value={day} onChange={(e) => setDay(e.target.value as Weekday)} className={SELECT}>
                    {WEEKDAYS.map((d) => (
                      <option key={d} value={d}>{d}</option>
                    ))}
                  </select>
                </div>
                <div className="relative flex h-5 rounded overflow-hidden border border-slate-600">
                  {runs.map((r) => (
                    <div
                      key={r.from}
                      style={{ width: `${((r.to - r.from) / 24) * 100}%` }}
                      title={`${hh(r.from)}-${hh(r.to)} ${nameOf(r.milestone) || 'no milestone'} (${r.rule === null ? 'otherwise' : `rule ${r.rule}`})`}
                      className={`${fill(r.milestone)} flex items-center justify-center overflow-hidden whitespace-nowrap text-[10px]`}
                    >
                      {r.to - r.from >= 6 ? nameOf(r.milestone) : ''}
                    </div>
                  ))}
                  {[4, 10, 20].map((h) => (
                    <span key={h} className="absolute inset-y-0 w-px bg-slate-900/70" style={{ left: `${(h / 24) * 100}%` }} />
                  ))}
                </div>
                <div className="relative h-3 text-[10px] text-slate-500">
                  {[0, 4, 10, 20, 24].map((h) => (
                    <span
                      key={h}
                      className="absolute"
                      style={{ left: `${(h / 24) * 100}%`, transform: h === 0 ? undefined : h === 24 ? 'translateX(-100%)' : 'translateX(-50%)' }}
                    >
                      {hh(h)}
                    </span>
                  ))}
                </div>
                <div className="flex items-center gap-3 flex-wrap text-xs text-slate-400">
                  {shown.map((id) => (
                    <span key={id} className="flex items-center gap-1">
                      <span className={`inline-block w-2.5 h-2.5 rounded-sm border border-slate-600 ${fill(id)}`} />
                      {nameOf(id) || 'no milestone'}
                    </span>
                  ))}
                </div>
                <div className="text-xs text-slate-500">
                  The preview ignores completion: the hunt skips a milestone complete on its game file, and ends when the default is complete and no rule matches.
                </div>
              </div>
              <div className="flex items-center gap-2 border-t border-slate-700/50 pt-1">
                <button
                  type="button"
                  onClick={() => save(draft)}
                  disabled={busy || (editing === null ? newName === '' || nameTaken : !contentDirty)}
                  className="px-2 py-0.5 bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
                >
                  {busy ? 'Saving...' : 'Save'}
                </button>
                {dirty && (
                  <button type="button" onClick={() => setDraft(baseline)} disabled={busy} className={BUTTON}>Discard</button>
                )}
                {editing !== null && (confirmingDelete ? (
                  <span className="flex items-center gap-1.5">
                    <span className="text-xs text-slate-300">Confirm?</span>
                    <button type="button" onClick={() => remove(editing)} disabled={busy} className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors font-semibold">Yes</button>
                    <button type="button" onClick={() => setConfirmingDelete(false)} className="text-xs text-slate-400 hover:text-slate-300 transition-colors">No</button>
                  </span>
                ) : (
                  <button type="button" onClick={() => setConfirmingDelete(true)} disabled={busy} className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors">Delete</button>
                ))}
              </div>
              {error && <div className="text-xs text-red-400">{error}</div>}
            </>
          )}
        </div>
      )}
    </div>
  )
}
