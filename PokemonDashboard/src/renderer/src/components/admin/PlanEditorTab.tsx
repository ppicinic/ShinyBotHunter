import React, { useEffect, useState } from 'react'
import { useAppContext } from '../../context/AppContext'
import * as api from '../../services/apiClient'
import type { AdminMetadata, EventArgumentOptions, GameFlavor, PhaseInfo, PickerMilestone, Plan, PlanTime, StepArguments, Weekday } from '../../types/api'
import { initialParamsFor, paramsFromArguments, stepArguments, WEEKDAYS } from './actionSchema'
import { ActionParamFields } from './ActionParamFields'

// One step as the form edits it: the params are the strings ActionParamFields edits, turned into the body only at Save.
export interface StepForm {
  times: PlanTime[]
  weekdays: Weekday[]
  action: string
  params: Record<string, string>
}

// The default's times and weekdays are always empty and never sent.
export interface PlanForm {
  name: string
  rules: StepForm[]
  default: StepForm
  endWhenComplete: boolean
}

const TIMES: PlanTime[] = ['Morning', 'Day', 'Night']

const SELECT = 'bg-card-border text-white text-xs rounded px-2 py-0.5 border border-slate-600 focus:outline-none focus:border-indigo-500'
const BUTTON = 'text-xs text-slate-400 hover:text-white px-1.5 py-0.5 rounded border border-slate-600 disabled:opacity-40'

// Every saved step's action is one of GET plans/steps: the save refuses any other.
const schemaOf = (steps: EventArgumentOptions[], action: string): EventArgumentOptions => steps.find((s) => s.name === action)!

export function formOf(plan: Plan, steps: EventArgumentOptions[]): PlanForm {
  const step = (times: PlanTime[], weekdays: Weekday[], args: StepArguments): StepForm => ({
    times,
    weekdays,
    action: args.eventName,
    params: paramsFromArguments(schemaOf(steps, args.eventName), args)
  })
  return {
    name: plan.name,
    rules: plan.rules.map((r) => step(r.times, r.weekdays, r.arguments)),
    default: step([], [], plan.default),
    endWhenComplete: plan.endWhenComplete
  }
}

export function planBody(form: PlanForm, game: GameFlavor, steps: EventArgumentOptions[]): Plan {
  const args = (s: StepForm): StepArguments => stepArguments(schemaOf(steps, s.action), s.params)
  return {
    game,
    name: form.name,
    rules: form.rules.map((r) => ({ times: r.times, weekdays: r.weekdays, arguments: args(r) })),
    default: args(form.default),
    endWhenComplete: form.endWhenComplete
  }
}

// A new action starts from its own seeds, so no field of the old action is sent with it.
export function withAction(step: StepForm, schema: EventArgumentOptions): StepForm {
  return { ...step, action: schema.name, params: initialParamsFor(schema) }
}

// A new rule, and a new plan's default, start as the first step action (Wild Encounter) with no times or days.
const newStep = (steps: EventArgumentOptions[]): StepForm => withAction({ times: [], weekdays: [], action: '', params: {} }, steps[0])
const newPlan = (steps: EventArgumentOptions[]): PlanForm => ({ name: '', rules: [], default: newStep(steps), endWhenComplete: false })

export function moveRule(rules: StepForm[], index: number, by: number): StepForm[] {
  const to = index + by
  if (to < 0 || to >= rules.length) return rules
  const moved = [...rules]
  ;[moved[index], moved[to]] = [moved[to], moved[index]]
  return moved
}

export function toggled<T>(list: T[], value: T, order: T[]): T[] {
  return order.filter((v) => (v === value) !== list.includes(v))
}

// A plan belongs to a game, not a file, so every milestone of the game is offered whatever its game-file ticks; Start checks
// those. An archived one is listed only while a step uses it, so the select never shows one milestone while holding another.
export function editorMilestones(phases: PhaseInfo[], used: string[]): PickerMilestone[] {
  return phases
    .filter((p) => !p.archived || used.includes(p.name))
    .map((p) => p.name)
    .sort((a, b) => a.localeCompare(b, undefined, { numeric: true }))
    .map((name) => ({ name, modality: null, huntable: true, complete: false, anchor: null, anchorSet: false }))
}

async function read<T>(r: Response): Promise<T> {
  if (!r.ok) throw new Error((await r.text()) || `Server returned ${r.status}`)
  return r.json() as Promise<T>
}

const message = (err: unknown): string => (err instanceof Error ? err.message : 'The request failed')

function Chip({ on, onClick, title, testId, children }: { on: boolean; onClick: () => void; title?: string; testId: string; children: React.ReactNode }): JSX.Element {
  return (
    <button
      type="button"
      data-testid={testId}
      onClick={onClick}
      title={title}
      aria-pressed={on}
      className={`text-xs px-2 py-0.5 rounded-full border transition-colors ${on ? 'bg-indigo-700 text-white border-indigo-500' : 'border-slate-600 text-slate-400 hover:text-white'}`}
    >
      {children}
    </button>
  )
}

// The action select and that action's own fields, drawn from its plans/steps schema.
function StepFields({ step, steps, milestones, onChange }: {
  step: StepForm
  steps: EventArgumentOptions[]
  milestones: PickerMilestone[]
  onChange: (step: StepForm) => void
}): JSX.Element {
  const schema = schemaOf(steps, step.action)
  return (
    <div className="space-y-1">
      <div className="flex items-center gap-2">
        <span className="text-xs text-slate-500 w-20 flex-shrink-0">Do</span>
        <select data-testid="plan-step-action" value={step.action} onChange={(e) => onChange(withAction(step, schemaOf(steps, e.target.value)))} className={`flex-1 ${SELECT}`}>
          {steps.map((s) => (
            <option key={s.name} value={s.name}>{s.name}</option>
          ))}
        </select>
      </div>
      {Object.keys(step.params).length === 0 ? (
        <div className="text-xs text-slate-500">(this step has no options)</div>
      ) : (
        <ActionParamFields
          schema={schema}
          params={step.params}
          onChange={(param, value) => onChange({ ...step, params: { ...step.params, [param]: value } })}
          availablePhases={milestones}
          party={undefined}
          partyLoading={false}
          offerIncompleteOnly={false}
        />
      )}
    </div>
  )
}

/** Admin > Plan Editor: a game's saved plans, each a list of time rules over whole hunts and a default. Nothing is written
 *  until Save, Rename or Delete, and every refusal shows the server's sentence. */
export function PlanEditorTab(): JSX.Element {
  const { config } = useAppContext()
  const apiUrl = config?.apiUrl ?? ''
  const [metadata, setMetadata] = useState<AdminMetadata | null>(null)
  const [steps, setSteps] = useState<EventArgumentOptions[]>([])
  const [game, setGame] = useState<GameFlavor | ''>('')
  const [plans, setPlans] = useState<Plan[]>([])
  // Bumped after each write, so the list is read again.
  const [listVersion, setListVersion] = useState(0)
  // The saved plan the form edits, by its saved name, or null for a new plan.
  const [editing, setEditing] = useState<string | null>(null)
  const [form, setForm] = useState<PlanForm | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  useEffect(() => {
    if (!apiUrl) return
    api.getMetadata(apiUrl).then((r) => read<AdminMetadata>(r)).then((m) => {
      setMetadata(m)
      setGame((g) => g || (Object.keys(m.games)[0] as GameFlavor | undefined) || '')
    }).catch((err) => setError(message(err)))
    api.getPlanSteps(apiUrl).then((r) => read<EventArgumentOptions[]>(r)).then((s) => {
      setSteps(s)
      setForm(newPlan(s))
    }).catch((err) => setError(message(err)))
  }, [apiUrl])

  useEffect(() => {
    if (!apiUrl || !game) return
    // A list read for a game no longer chosen is dropped.
    let current = true
    api.getPlans(apiUrl, game).then((r) => read<Plan[]>(r)).then((list) => {
      if (current) setPlans(list)
    }).catch((err) => {
      if (current) setError(message(err))
    })
    return () => {
      current = false
    }
  }, [apiUrl, game, listVersion])

  const choose = (name: string): void => {
    const plan = plans.find((p) => p.name === name)
    setEditing(plan ? plan.name : null)
    setForm(plan ? formOf(plan, steps) : newPlan(steps))
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
      setListVersion((v) => v + 1)
    } catch (err) {
      setError(message(err))
    } finally {
      setBusy(false)
    }
  }

  if (!metadata || !form || !game) {
    return <div className="p-3 text-xs text-slate-500">{error ?? 'Loading...'}</div>
  }

  const name = form.name.trim()
  // A saved plan is saved under its saved name; a changed name waits for Rename.
  const saveName = editing ?? name
  const nameTaken = editing === null && plans.some((p) => p.name === name)
  const used = [form.default, ...form.rules].map((s) => s.params['PhaseName']).filter((m): m is string => !!m)
  const milestones = editorMilestones(metadata.games[game]?.phases ?? [], used)
  const setRule = (index: number, rule: StepForm): void => setForm({ ...form, rules: form.rules.map((r, i) => (i === index ? rule : r)) })

  const save = (): Promise<void> => send(() => api.savePlan(apiUrl, game, saveName, planBody(form, game, steps)), () => {
    setEditing(saveName)
    setForm({ ...form, name: saveName })
  })
  const rename = (from: string): Promise<void> => send(() => api.renamePlan(apiUrl, game, from, name), () => {
    setEditing(name)
    setForm({ ...form, name })
  })
  const remove = (from: string): Promise<void> => send(() => api.deletePlan(apiUrl, game, from), () => {
    setEditing(null)
    setForm(newPlan(steps))
    setConfirmingDelete(false)
  })

  return (
    <div className="p-3 space-y-2">
      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-xs text-slate-500">Game</span>
        <select
          data-testid="plan-game"
          value={game}
          onChange={(e) => {
            setGame(e.target.value as GameFlavor)
            setPlans([])
            setEditing(null)
            setForm(newPlan(steps))
            setError(null)
            setConfirmingDelete(false)
          }}
          disabled={busy}
          className={SELECT}
        >
          {Object.keys(metadata.games).map((g) => (
            <option key={g} value={g}>{g}</option>
          ))}
        </select>
        <span className="text-xs text-slate-500">Plan</span>
        <select data-testid="plan-select" value={editing ?? ''} onChange={(e) => choose(e.target.value)} disabled={busy} className={SELECT}>
          {plans.map((p) => (
            <option key={p.name} value={p.name}>{p.name}</option>
          ))}
          <option value="">New plan...</option>
        </select>
        <input
          data-testid="plan-name"
          type="text"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          placeholder="Plan name"
          className={`flex-1 placeholder-slate-500 ${SELECT}`}
        />
        {editing !== null && name !== '' && name !== editing && (
          <button type="button" data-testid="plan-rename" onClick={() => rename(editing)} disabled={busy} className={BUTTON}>Rename</button>
        )}
        {nameTaken && <span className="text-xs text-amber-400">already a plan</span>}
        {editing !== null && (confirmingDelete ? (
          <span className="flex items-center gap-1.5">
            <span className="text-xs text-slate-300">Delete?</span>
            <button type="button" data-testid="plan-delete-yes" onClick={() => remove(editing)} disabled={busy} className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors font-semibold">Yes</button>
            <button type="button" data-testid="plan-delete-no" onClick={() => setConfirmingDelete(false)} className="text-xs text-slate-400 hover:text-slate-300 transition-colors">No</button>
          </span>
        ) : (
          <button type="button" data-testid="plan-delete" onClick={() => setConfirmingDelete(true)} disabled={busy} className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50 transition-colors">Delete</button>
        ))}
      </div>
      <p className="text-xs text-slate-500">
        A plan is a list of hunts with time rules. Each step is the same form Bot Control shows for that hunt. Rules are checked
        top to bottom, first match wins; the default runs when none matches. Times are the in-game clock: morning 4:00-10:00,
        day 10:00-20:00, night 20:00-4:00.
      </p>
      {form.rules.map((rule, i) => (
        <div key={i} data-testid={`plan-rule-${i + 1}`} className="border border-slate-700/60 rounded px-2 py-1.5 space-y-1">
          <div className="flex items-center gap-1.5">
            <span className="text-xs text-slate-300">Rule {i + 1}</span>
            <button type="button" data-testid="plan-rule-up" onClick={() => setForm({ ...form, rules: moveRule(form.rules, i, -1) })} disabled={i === 0} className={BUTTON}>Up</button>
            <button type="button" data-testid="plan-rule-down" onClick={() => setForm({ ...form, rules: moveRule(form.rules, i, 1) })} disabled={i === form.rules.length - 1} className={BUTTON}>Down</button>
            <button
              type="button"
              data-testid="plan-rule-remove"
              onClick={() => setForm({ ...form, rules: form.rules.filter((_, j) => j !== i) })}
              className="ml-auto text-xs text-red-400 hover:text-red-300 transition-colors"
            >
              Remove
            </button>
          </div>
          <div className="flex items-center gap-1 flex-wrap">
            <span className="text-xs text-slate-500 w-20 flex-shrink-0">When</span>
            {TIMES.map((time) => (
              <Chip key={time} testId={`plan-time-${time}`} on={rule.times.includes(time)} onClick={() => setRule(i, { ...rule, times: toggled(rule.times, time, TIMES) })}>
                {time}
              </Chip>
            ))}
            {rule.times.length === 0 && <span className="text-xs text-slate-500">none on: any time</span>}
          </div>
          <div className="flex items-center gap-1 flex-wrap">
            <span className="text-xs text-slate-500 w-20 flex-shrink-0">Days</span>
            {WEEKDAYS.map((day) => (
              <Chip key={day} testId={`plan-day-${day}`} on={rule.weekdays.includes(day)} title={day} onClick={() => setRule(i, { ...rule, weekdays: toggled(rule.weekdays, day, WEEKDAYS) })}>
                {day.slice(0, 2)}
              </Chip>
            ))}
            {rule.weekdays.length === 0 && <span className="text-xs text-slate-500">none on: any day</span>}
          </div>
          <StepFields step={rule} steps={steps} milestones={milestones} onChange={(s) => setRule(i, s)} />
        </div>
      ))}
      <div data-testid="plan-default" className="border border-slate-700/60 rounded px-2 py-1.5 space-y-1">
        <span className="text-xs text-slate-300">Default: when no rule matches</span>
        <StepFields step={form.default} steps={steps} milestones={milestones} onChange={(s) => setForm({ ...form, default: s })} />
      </div>
      <div className="flex items-center gap-2">
        <span className="text-xs text-slate-500 w-20 flex-shrink-0">Plan ends</span>
        <select
          data-testid="plan-ends"
          value={form.endWhenComplete ? 'complete' : 'never'}
          onChange={(e) => setForm({ ...form, endWhenComplete: e.target.value === 'complete' })}
          className={SELECT}
        >
          <option value="never">Never (runs until cancelled)</option>
          <option value="complete">When every shiny-hunt step is complete</option>
        </select>
      </div>
      <div className="flex items-center gap-2">
        <button type="button" data-testid="plan-add-rule" onClick={() => setForm({ ...form, rules: [...form.rules, newStep(steps)] })} className={BUTTON}>+ Add a rule</button>
        <button
          type="button"
          data-testid="plan-save"
          onClick={() => save()}
          disabled={busy || saveName === '' || nameTaken}
          className="px-2 py-0.5 bg-indigo-700 hover:bg-indigo-600 disabled:opacity-50 text-white text-xs rounded transition-colors"
        >
          {busy ? 'Saving...' : 'Save plan'}
        </button>
      </div>
      {error && <div data-testid="plan-error" className="text-xs text-red-400">{error}</div>}
    </div>
  )
}
