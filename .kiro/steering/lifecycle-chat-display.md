---
inclusion: always
---
# Relaying test-page chat renders

When a notification from a test-page workflow step contains one or more lines `CHAT-RENDER <path>`:

1. For each CHAT-RENDER file, in the order listed:
   a. Read the file with the file-read tool.
   b. Publish it with the `create_artifact` tool: kind `markdown`, title = line 1 of the file
      without the leading `### `, content = the file's markdown unchanged.
   c. Embed the artifact in the chat on its own line: `![<title>](kiro-artifact://<id>)`
      (the id returned by `create_artifact`).
   d. Below the embed, paste the file's markdown verbatim - every table and row, no
      summarizing, no reformatting, no omissions.
   The artifact renders as its own block in the chat and full-size in the artifact panel, so
   its tables stay intact even when the chat inserts a workflow update card into the message.
2. One CHAT-RENDER file = one view. Each report section is ONE table (tables are never split).
   Only the plan arrives as several files: an overview, then one file per module. Relay every
   file as its own section (steps a-d), add a blank line, then the next file. Never merge,
   reorder or skip files.
3. After all files of the notification, add at most one sentence of your own (for example
   the step's summary line).
4. At a gate (the notification is a warning / needs input), relay every file first, then ask
   the question from the notification and wait for the reply.
5. While relaying, do not run shell commands, extra checks or other tools besides reading
   those files and `create_artifact`.

Multiple CHAT-RENDER notifications are relayed one by one in the order received. During the
run, live status tables (`NN-live-status.md`) arrive as modules finish; the full module
results follow afterwards in plan order (A, B, C, ...), then the run summary.

If a table still shows as raw `| ... |` text in the chat, open the artifact (or the
`chat/*.md` file itself): the markdown is valid, the chat split the message where it inserted
a workflow update card.
