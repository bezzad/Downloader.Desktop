# huggingface-model-download Specification

## Purpose
TBD - created by archiving change fix-intercept-and-plugin-gaps. Update Purpose after archive.
## Requirements
### Requirement: HuggingFace model repositories are claimed

The Ollama plugin's resolver SHALL claim `https://huggingface.co/<owner>/<repo>` URLs, including links
that name a revision or a file inside the repository. The claim check SHALL be fast and SHALL NOT perform
network I/O. A HuggingFace URL that is not a model repository — a dataset, a space, a user profile, the
site's own pages — SHALL NOT be claimed.

#### Scenario: A model repository link is claimed
- **WHEN** the plugin is asked whether it can resolve `https://huggingface.co/empero-ai/Qwen3.8-2B-Distill-GGUF`
- **THEN** it returns true, without making a network request

#### Scenario: A direct file link inside a repository is claimed
- **WHEN** the link names a file inside a model repository, such as a `resolve/main/<file>.gguf` address
- **THEN** it is claimed and resolves to that file

#### Scenario: A non-model HuggingFace page is not claimed
- **WHEN** the link is a dataset, a space, or the HuggingFace home page
- **THEN** it is not claimed, and the app treats it as an ordinary URL

### Requirement: A repository's model files are offered as selectable variants

Resolving a claimed repository SHALL list the repository's downloadable model files and offer them as
variants for the user to choose from, named so that the choice is meaningful — the quantisation and the
file's size. When the repository holds exactly one such file it SHALL be resolved directly, without asking.
A repository that holds no downloadable model file SHALL fail with a message saying so.

#### Scenario: A repository with several quantisations asks which one
- **WHEN** a claimed repository contains several GGUF files
- **THEN** the user is offered one variant per file, each showing its quantisation and size
- **AND** the chosen variant is what gets downloaded

#### Scenario: A single-file repository downloads straight away
- **WHEN** a claimed repository contains exactly one model file
- **THEN** it resolves to that file with no variant prompt

#### Scenario: A repository with no model file fails clearly
- **WHEN** a claimed repository contains no downloadable model file
- **THEN** the download fails with a message naming the repository and saying no model file was found

#### Scenario: A private or missing repository fails clearly
- **WHEN** the repository does not exist or requires credentials the app does not have
- **THEN** the failure message says which of the two it was

### Requirement: A downloaded HuggingFace model can be added to local Ollama

A completed HuggingFace model download SHALL offer the same explicit "Add to Ollama" action as an
ollama.com model, installing the downloaded file into the local Ollama store under a name derived from the
repository and the chosen file, so it appears in `ollama list`. The user's downloaded file SHALL NOT be
moved or deleted. Because a HuggingFace file carries no Ollama manifest, integrity SHALL be checked against
what the repository itself publishes for that file.

#### Scenario: A downloaded model is installed
- **WHEN** the user triggers "Add to Ollama" on a completed HuggingFace model download
- **THEN** the model is present in the local Ollama store and appears in `ollama list`
- **AND** the user's downloaded file remains in their save folder

#### Scenario: A file that does not match what the repository published is refused
- **WHEN** the downloaded file's checksum does not match the one the repository publishes for it
- **THEN** the action fails with a clear message and nothing is written into the store

#### Scenario: Ollama is not installed
- **WHEN** no Ollama store can be found or created
- **THEN** the action fails with a message explaining Ollama was not found and where it looked

