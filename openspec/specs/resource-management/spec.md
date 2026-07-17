# resource-management Specification

## Purpose
Keep the app's memory bounded over long sessions: engine resources tied to a download are released as soon as they are no longer needed.

## Requirements

### Requirement: Engine instances are released on terminal state
When a download reaches a terminal state (Completed, Failed, or Stopped), the app SHALL dispose its engine instance (`DownloadService`) and release its download package and buffers. The row SHALL retain only the lightweight persisted fields (name, size, downloaded, progress, status, folder, urls) needed to display and to resume. Memory SHALL NOT grow unboundedly with the number of completed downloads.

#### Scenario: Memory returns toward baseline after many completions
- **WHEN** many downloads complete in one session
- **THEN** the managed heap after completion (following a forced collection) is bounded near the pre-batch baseline rather than increasing per completed download

#### Scenario: A released download can still be resumed or retried
- **WHEN** a Stopped or Failed download whose engine was released is resumed or retried
- **THEN** a fresh engine is built and the download continues from the existing partial file (auto-resume), producing a correct final file
