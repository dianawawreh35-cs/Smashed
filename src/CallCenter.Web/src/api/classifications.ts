/**
 * The classification form and its types (A-40, S-40). Mirrors
 * `CallCenter.Shared.Contracts.Classifications` — hand-written, so a change on
 * the server has to be copied across.
 */
import { api } from './client'

export interface ClassificationType {
  id: string
  /** The stable key reports are written against. Set once, never renamed. */
  name: string
  labelAr: string
  labelEn: string
  colour: string | null
  /** One of the six the system was built around: renameable, never deletable. */
  isSystem: boolean
  sortOrder: number
  isActive: boolean
  /** Some call already uses it, so it can be hidden but not deleted. */
  inUse: boolean
}

export interface FormBranch {
  id: string
  name: string
}

/** What each kind of question is called on the wire. */
export type FieldKind =
  | 'type'
  | 'branch'
  | 'text'
  | 'textarea'
  | 'number'
  | 'select'
  | 'checkbox'

export interface FieldOption {
  value: string
  label?: { ar?: string; en?: string }
}

export interface FormField {
  /** Stable within the form. Answers are stored against it. */
  key: string
  kind: FieldKind
  label?: { ar?: string; en?: string }
  required?: boolean
  /**
   * The type *names* this field is asked for; absent means always. Names, not
   * labels, so renaming a type never makes a field disappear.
   */
  showWhenType?: string[]
  options?: FieldOption[]
}

export interface FormDefinition {
  fields: FormField[]
}

/**
 * Which conversations a form is for. Inbound and outbound calls have their own
 * forms: a call the agent placed is a different conversation from an order
 * coming in. `None` is the third form, for messages (A-70): a conversation on
 * WhatsApp or another app has no direction and its own questions. The types
 * are shared between all three.
 */
export type FormDirection = 'In' | 'Out' | 'None'

export interface ClassificationForm {
  version: number
  definition: FormDefinition
  types: ClassificationType[]
  branches: FormBranch[]
  direction: FormDirection
}

export const getClassificationForm = (direction: FormDirection = 'In') =>
  api.get<ClassificationForm>('/classifications/form', { query: { direction } })

/**
 * Publishes a new version of one direction's form (S-40).
 *
 * Never an edit in place: existing classifications keep the version they were
 * captured under, so a complaint classified in January still reads back with
 * January's questions.
 */
export const publishClassificationForm = (
  definition: FormDefinition,
  direction: FormDirection = 'In',
) => api.put<ClassificationForm>('/classifications/form', { definition, direction })

export const listClassificationTypes = () =>
  api.get<ClassificationType[]>('/classifications/types', {
    query: { includeInactive: true },
  })

export interface UpsertClassificationTypeRequest {
  labelAr: string
  labelEn: string
  colour?: string | null
  sortOrder: number
  isActive: boolean
  /** Only honoured on create. */
  name?: string
}

export const createClassificationType = (request: UpsertClassificationTypeRequest) =>
  api.post<ClassificationType>('/classifications/types', request)

export const updateClassificationType = (
  id: string,
  request: UpsertClassificationTypeRequest,
) => api.put<ClassificationType>(`/classifications/types/${id}`, request)

export const deleteClassificationType = (id: string) =>
  api.delete<void>(`/classifications/types/${id}`)
