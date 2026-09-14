# Data

Local, per-agent state held in a SQLite database under
`%LOCALAPPDATA%\CallCenter\agent-buffer.db`.

Its job is the **offline buffer**: when the server is unreachable, call events,
classifications and notes recorded by the agent are queued here and replayed
once the connection returns, so no interaction is lost during a network or
server outage.

Added in a later prompt: `AgentBufferDbContext`, the buffered-event entity, and
the sync service that drains it against the API.
