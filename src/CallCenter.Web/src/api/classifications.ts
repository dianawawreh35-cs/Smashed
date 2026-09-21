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

export interface ClassificationForm {
  version: number
  definition: FormDefinition
  types: ClassificationType[]
  branches: FormBranch[]
}

export const getClassificationForm = () =>
  api.get<ClassificationForm>('/classifications/form')

/**
 * Publishes a new version of the form (S-40).
 *
 * Never an edit in place: existing classifications keep the version they were
 * captured under, so a complaint classified in January still reads back with
 * January's questions.
 */
export const publishClassificationForm = (definition: FormDefinition) =>
  api.put<ClassificationForm>('/classifications/form', { definition })

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
