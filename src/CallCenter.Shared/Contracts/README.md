# CallCenter.Shared.Contracts

DTO records shared between the ASP.NET Core API (`CallCenter.Server`), the WPF
Agent App (`CallCenter.AgentApp`) and — via hand-written TypeScript types — the
supervisor SPA (`CallCenter.Web`).

Contracts are added alongside the feature that first needs them. Keep them as
`record` types with init-only properties, no EF Core or SIPSorcery references,
and no behaviour beyond validation attributes.

**Here now:** [`Auth/`](Auth/) — `LoginRequest`, `LoginResponse`, `CurrentUserDto`,
`AgentExtensionsDto`, `LogoutRequest` and the `LogoutReasons` constants.
`AgentExtensionsDto` is the one DTO that carries secrets: it is returned by
`POST /api/auth/login` to an agent and nowhere else (N-05).

## Planned DTOs

Grouped by the `Features/` folder that will own them, and by the tables in
[`docs/SCHEMA.md`](../../../docs/SCHEMA.md) they project.

| Feature | Tables | Planned records |
| --- | --- | --- |
| Auth | `users`, `agent_sessions` | `LoginRequest`, `LoginResponse`, `CurrentUserDto`, `AgentExtensionsDto` (customer/internal extension + secret, agent only), `LogoutRequest` |
| Users | `users`, `branches` | `UserDto`, `CreateUserRequest`, `UpdateUserRequest`, `AgentSessionDto`, `AgentPresenceDto` |
| Contacts | `contacts`, `contact_phones` | `ContactDto`, `ContactSummaryDto`, `ContactPhoneDto`, `ContactSearchRequest`, `UpsertContactRequest`, `MergeContactsRequest`, `SetContactFlagRequest`, `BlockedNumbersDto` (cache pushed to the Agent App) |
| Communications | `communications`, `recordings` | `CommunicationDto`, `CommunicationDetailDto`, `CommunicationListRequest`, `LogCommunicationRequest`, `RecordingDto` |
| Classifications | `classification_types`, `form_definitions`, `classifications`, `classification_history` | `ClassificationTypeDto`, `FormDefinitionDto`, `FormFieldDto`, `ClassificationDto`, `SaveClassificationRequest`, `ClassificationHistoryDto` |
| Tasks | `follow_up_tasks` | `FollowUpTaskDto`, `CreateTaskRequest`, `UpdateTaskRequest`, `CloseTaskRequest` |
| Reports | all (read models) | `ReportRangeRequest`, plus one DTO per report R‑01 … R‑21 |
| Pbx | `pbx_events_raw` | `PbxChannelEventDto`, `OriginateCallRequest`, `PbxStatusDto` |
| Settings | `settings`, `branches`, `channels` | `SettingDto`, `UpdateSettingsRequest`, `BranchDto`, `ChannelDto` |
| Realtime (`AgentHub`) | — | `IncomingCallNotification`, `CallStateChanged`, `AgentStateChanged`, `TaskAssigned`, `BlockedListChanged`, `ForceLogout` |
| Offline buffer | `outbox_sync` | `BufferedOperation` (carries the laptop-generated `client_op_id`), `SyncBatchRequest`, `SyncBatchResponse` |

Enumerated fields use the constants in [`Enums.cs`](../Enums.cs), which mirror
the CHECK constraints in the schema — never raw strings.

Requirement IDs come from [`docs/SRS-Smashed-Burger-Call-Center.md`](../../../docs/SRS-Smashed-Burger-Call-Center.md)
— e.g. the Auth DTOs serve A-01/A-05/S-01, and the Reports DTOs serve R-01 … R-21.
