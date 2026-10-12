# AI: image descriptions, questions about the screen and page summaries

Vox can describe pictures, answer questions about what is on the screen and summarise web pages using an AI model. It uses a model on your own computer when you have set one up; otherwise it can use a cloud provider (Claude, from Anthropic) with your own API key. **Nothing is sent off your computer without your say-so**, and some things are never sent at all.

## Commands

| Key | What it does |
| --- | --- |
| Insert+G | Describe the navigator object. While browsing a page: the graphic at the browse cursor, or else the page as it is shown. |
| Insert+Alt+G | Ask a question about the window in front ("how do I attach a file?"). |
| Insert+Ctrl+I | Summarise the page being browsed (browse mode). |
| Insert+Ctrl+M | Move to where the page's main content starts; press again for the next suggestion (browse mode). |

Vox says "Describing image", "Thinking" or "Summarizing" and plays the progress sound every couple of seconds until the answer comes. Pressing the same key again cancels ("Cancelled"), and so does Ctrl (stop speech). The answer is spoken and put under the review cursor, like recognised text (`docs/ocr.md`): the review keys read it again line by line, word by word or character by character, and a braille display shows it. The review cursor goes back to what it was reviewing when focus, the caret, the review mode or the browse cursor moves.

The same picture, question and page are answered from memory the second time (for this session only), so pressing Insert+G twice on the same image doesn't ask again.

## Setting it up

### A model on this computer

Vox talks to [Ollama](https://ollama.com) (or anything offering its `/api/chat` API). Install it, pull a vision model (for example `ollama pull llama3.2-vision` or `ollama pull gemma3`), and set in `settings.json`:

```json
"AiLocalModel": "llama3.2-vision",
"AiLocalEndpoint": "http://localhost:11434"
```

Only an address on this computer (`localhost`, `127.x.x.x`, `::1`) is ever used; any other address is ignored. A local model needs no consent, works with the network turned off, and is used before any cloud provider. Local models are slower than cloud ones, especially without a graphics card.

Windows' own on-device AI APIs (Windows App SDK image description, on Copilot+ PCs) need an application with package identity, which Vox's MSI install doesn't have, so they aren't used yet.

### Claude (cloud)

1. Get an API key from the [Claude Console](https://platform.claude.com).
2. Vox menu (Insert+N), **Set AI provider key**, paste it and press Enter. Vox keeps it encrypted for your Windows account (`ai-key.dat` beside your settings, DPAPI); it is never written to `settings.json` or the log. Saving a key also sets **Cloud AI provider** to Claude.
3. The first time you use an AI command in a session, Vox asks whether it may send what it needs (a picture of the object or window, the window's text, or the page's text). Yes holds until Vox restarts.

Settings (Insert+Ctrl+G, AI page):

- **Cloud AI provider**: None (only a model on this computer) or Claude.
- **Send to the cloud AI provider**: *Ask first, once per session* (default), *Always, without asking*, or *Never*.
- **Announce CAPTCHAs and their accessible alternatives**: on by default.

In `settings.json` only: `AiModel` (default `claude-opus-5-5`), `AiEndpoint` (default `https://api.anthropic.com`; for a proxy or gateway), `AiPrivateApps` (see below). To forget the key, delete `ai-key.dat` from Vox's settings folder, or set the provider to None.

Requests use low effort for quick answers and, on the Claude API, opt in to Anthropic's server-side fallback (`fallbacks: "default"`), so a request one model declines can be answered by the model Anthropic recommends. A declined request is reported as "Claude declined to answer".

## What is never sent

Whatever the settings say:

- **Password fields**: Insert+G on a password field, or a question while the focus is in one, sends nothing ("Password fields are never sent").
- **Private applications**: windows of the applications in `AiPrivateApps` (process names; KeePass, KeePassXC, 1Password and Bitwarden by default) are never sent to the cloud.
- **The sign-in, lock and UAC screens**: no AI at all, local or cloud.
- **Organization policy**: `DisableAI` turns every AI feature off; `DisableNetwork` leaves only a model on this computer (`docs/enterprise.md`).

Only the area being described is captured, when you press the key. Pictures, questions and answers stay in memory; they are never written to disk or the log.

AI settings are the same in every configuration profile: a profile can't turn cloud sending on.

## Page summaries and main content

Insert+Ctrl+I summarises the page with the model on this computer, else Claude (with consent). With neither, or when you say no, Vox summarises the page from its structure: title, main heading, reading time, sections (level 2 headings) and how the main content opens, ending with the key that moves there.

Insert+Ctrl+M moves to where the main content most likely starts: the main landmark, then the first level 1 heading outside navigation, then the longest passage of prose. Each press goes to the next suggestion, saying which it is.

## CAPTCHAs

When a page has a CAPTCHA, Vox says so once, with the accessible way through:

- **reCAPTCHA**: check "I'm not a robot"; a picture challenge has a "Get an audio challenge" button.
- **hCaptcha**: no audio challenge; its accessibility option (sign up once at hcaptcha.com/accessibility) lets you through.
- **Cloudflare Turnstile**: usually passes by itself; check the box if one appears.
- **Others**: Vox names the audio alternative when it finds one beside the CAPTCHA, or suggests asking the site for help.

Vox never tries to solve a CAPTCHA.

## How it works

- `IImageDescriber` (`Ai/`): `LocalImageDescriber` (Ollama `/api/chat`, loopback only) and `CloudImageDescriber` (Claude Messages API; raw HTTP because every connection must go through `NetworkGate`'s `HttpClient`). Both send a PNG (`PngEncoder`, scaled to at most 1568 pixels for Claude, 1024 locally) and the instructions in `AiPrompts` (plain text for speech and braille, in Vox's language).
- `AiAssistant` is the only caller: the local describer first, else the cloud one after `AiConsent.ForCloud` (password field, private app, network, the consent setting) and, in Ask mode, `IAiConsentPrompt` (`AiConsentPrompt`, a yes/no Vox dialog). Answers are cached in `DescriptionCache` (in memory, least recently used out first, keyed by image hash, task, question, text, language and describer).
- `AiCommands` captures through `IScreenCapture` and `IOcrTargetSource` (shared with text recognition), learns about the target from `IAiContextSource` (`UIAAiContextSource`: password state, foreground process, the window's text from `IReviewTextSource`) and shows answers with `ReviewCommands.ShowRecognizedAsync`. The question and key prompts are `TextPromptDialog`s shown through `OwnDialogRunner`, which turns browse keys off while they are open.
- `CaptchaDetector`, `MainContent` and `PageDigest` (`Navigation/`) work from the buffer alone; `BrowseModeController` announces CAPTCHAs after a load or update and runs Insert+Ctrl+M.
