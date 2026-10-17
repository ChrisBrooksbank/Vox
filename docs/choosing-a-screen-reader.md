# Which screen reader should you use on Windows in 2026?

*JAWS, NVDA, Narrator, the magnifier suites, and a newcomer called Vox: what each one is good
at, what it costs you, and whether it's worth switching.*

---

Ask a room of blind Windows users which screen reader is best and you'll start an argument
that won't end before the coffee runs out. Ask them why they use the one they use, and the
answer is much duller: it's the one they already know. In WebAIM's latest screen reader
survey, run in the summer of 2026 with nearly 1,800 respondents, about half said comfort and
existing expertise was the main reason for their choice. Features came a distant second. Cost
and support barely registered for most people.

That's not irrationality. A screen reader isn't an app you try for an afternoon. It's the way
you hear your whole computer, and the keys are in your fingers, not your head. Switching costs
weeks of slower work, and nobody wants that for a marginal gain.

So this guide isn't a league table. It's an attempt to answer two practical questions: if you
were choosing from scratch today, what would suit you? And if you already have a screen reader,
is anything out there good enough to justify the disruption?

Full disclosure: this article lives in the repository of Vox, one of the screen readers it
reviews. We've tried hard to be fair to the others, and to be blunt about Vox's own
weaknesses. You'll find plenty of them below.

## The lie of the land

On the desktop, Windows is still where most screen reader users live, and two products
dominate it. In the 2026 WebAIM survey, **JAWS** was the primary screen reader for 55% of
respondents and **NVDA** for about 33%. Apple's **VoiceOver** came third, at around 6.5%, and
everything else was a rounding error: ZoomText and Fusion about 2%, Orca on Linux under 1.5%,
**Narrator** well under 1%, Dolphin SuperNova lower still.

Primary use hides a more interesting fact. Seven in ten respondents use more than one desktop
screen reader, and more than a third use Narrator at least some of the time. People keep a
second reader around for the website the first one stumbles on, for a colleague's computer,
or for when the main one crashes. That matters when you think about switching: it doesn't have
to be all or nothing.

A caveat about those numbers: WebAIM's sample isn't controlled, and it leans heavily toward
North America, where JAWS is often bought by employers and schools. Elsewhere in the world,
and among people paying their own way, NVDA's share is likely higher.

## The contenders

### JAWS: the professional's tool

JAWS (Job Access With Speech) shipped in 1989 and has spent 35 years becoming the screen reader
employers buy. Its maker, Freedom Scientific, now part of Vispero, sells it with training,
certification and phone support, which is exactly what an IT department wants to hear.

**Where it shines.** Microsoft Office, above all. Decades of application scripts mean JAWS
knows Word's comments and tracked changes, Excel's row and column titles, and Outlook's quirks
better than anything else. Its scripting language lets organisations make in-house
applications usable that no other reader can touch. The 2026 release leans heavily into AI:
**Page Explorer** gives an overview of a web page's layout and main content and lets you ask
follow-up questions, **FSCompanion** is an AI assistant for learning JAWS itself, and **Picture
Smart AI** describes images and whole screens with AI.

**What it costs.** Money, and some freedom. When we checked, Vispero's US store listed a JAWS
Home Annual licence at about $105 a year, for non-commercial use on up to three of your own
computers; professional and corporate licences cost considerably more. Late 2025 brought
changes that went down badly with some users: a new Vispero account, rolled out so abruptly
that the company apologised for it; live support limited to people with a current
subscription or maintenance agreement; and an end to upgrading old perpetual licences without
one. In the US, JAWS 2026 asks every user to sign in to a Vispero account, and since May 2026
some of its AI features also depend on the age set on that account.

**Choose it if** your employer provides it, you work all day in Office or in-house
applications, or you value having someone to phone when it goes wrong.

### NVDA: the free heavyweight

NVDA (NonVisual Desktop Access) was started in 2006 by two blind Australians, Michael Curran
and James Teh, who wanted a screen reader nobody would have to pay for. Twenty years later it's
used in more than 60 languages and is, for most of the world, *the* Windows screen reader.

**Where it shines.** The web, where its browse mode set the conventions most readers now
follow. Its add-on store holds hundreds of community extensions, so if NVDA doesn't do
something, someone has often written an add-on that does. It's free and open source, runs from
a USB stick, and is quick to adopt new Windows and browser features. 2026 has been a big year:
version 2026.1, released in May, was the first 64-bit NVDA and built in the MathCAT math
reader; version 2026.2, released at the end of August, added a built-in full-screen magnifier,
richer touch gestures and automatic braille scrolling.

**What it costs.** Nothing, though NV Access asks for donations and sells training. The real
cost is time: there's no phone line, support comes from the community, and the 64-bit move
broke compatibility with many add-ons. When 2026.1 shipped, only around a third of the store's
add-ons had been updated for it, which left some users choosing between the new NVDA and their
favourite extensions. AI image description is still something you get from add-ons, not NVDA
itself: an on-device describer was tested in alpha builds in late 2025 but hadn't reached a
stable release by October 2026.

**Choose it if** you want the most capable reader that costs nothing, browse the web a lot, or
like tailoring your tools.

### Narrator: the one that's already there

Narrator ships with Windows, which is its superpower: it's on every computer, including the
one in the library and the one you've just bought. Press **Ctrl+Windows+Enter** and it talks.

**Where it shines.** It has improved a great deal in recent years. During 2025 Microsoft added
**Screen Curtain** (blacking out the display for privacy), **Speech Recap** for reviewing what
was said, better Word support, and natural-sounding HD voices. On Copilot+ PCs, Narrator can
describe images, charts and the whole screen on the device itself; on other PCs it hands the
picture to Copilot. It's also what most installers, including NVDA's and Vox's, rely on to get
you started.

**What it costs.** Nothing beyond Windows. But it has no scripting or add-ons, a thinner web
browse mode than NVDA or JAWS, and less control over speech and pronunciation. Very few people
make it their only reader, which tells you something.

**Choose it if** you need a screen reader occasionally, are setting up a computer, or want a
dependable backup.

### SuperNova and ZoomText Fusion: for people who still use their eyes

If you have some useful vision, a pure screen reader may not be what you want. **Dolphin
SuperNova** and Vispero's **Fusion** (ZoomText's magnifier combined with JAWS) are built for
people who mix magnification and speech, and for people whose sight is changing. Both are paid
products sold with maintenance agreements, both are popular with sight-loss charities and
rehabilitation services, and Dolphin markets SuperNova partly as a lower-cost alternative to
Vispero's suite. NVDA's new built-in magnifier is a start, but it's full-screen only for now.

**Choose one if** magnification is part of how you use a computer every day.

### Vox: the newcomer

Vox is the new arrival, and this is its home repository. It's a free, open-source (MIT
licensed) screen reader for Windows 11, written from scratch in C# and .NET. Its first public
release, version 0.1, came out in October 2026.

**What it is trying to do.** Vox deliberately follows NVDA's keyboard conventions, so an NVDA
user's habits carry over, and it also offers a JAWS keyboard layout for people coming from
JAWS. It's built on modern Windows accessibility (UI Automation) rather than the older
techniques the veterans grew up with, and it puts a lot of effort into not stranding you: a
watchdog restarts it after a crash, it says "not responding" instead of going silent when an
application hangs, it notices when Windows quietly disconnects its keyboard hook, and an
add-on that keeps failing is switched off.

**What's in the box.** Rather than relying on add-ons, Vox builds in many of the features
NVDA users install separately:

- browse mode for Edge, Chrome and Firefox with more than 20 kinds of single-key navigation,
  tables read with their headers, and cookie banners announced and dismissable with one key;
- support for Word, Excel, Outlook, PowerPoint, Teams, Zoom, Visual Studio Code and Acrobat;
- braille through HID displays and BRLTTY, with contracted braille through liblouis;
- text recognition (OCR) that runs on your computer;
- optional AI image descriptions, page summaries and "ask the page" questions, using either a
  model on your own computer or a cloud service, off until you set them up and asking before
  anything leaves your machine;
- remote access between two Vox computers, for trainers and helpers;
- a built-in magnifier (docked, lens or full screen, with colour filters), a screen curtain
  that blanks the display while Vox keeps reading, and touch gestures on touch screens;
- speech on demand for meetings, a key that mutes the microphone in Teams, Zoom or any
  application, and spoken confirmation of cut, copy and paste;
- "Guide me": say what you want to do on a page and get short numbered steps, each offering to
  take you to its element;
- configuration profiles, per-site settings, custom labels for unlabelled controls, and its
  own signed add-on store;
- Group Policy, silent install and locked settings for organisations;
- no telemetry, and it never logs what you type.

**Switching is designed in.** Vox can import your NVDA or JAWS settings: voice, rate, pitch,
punctuation, typing echo, your own key assignments and your pronunciation dictionaries. It
shows you a report of what it brought over and what it couldn't before changing anything. A
trial mode stops your current screen reader while you try Vox and brings it back with one key.

**The honest weaknesses.** Vox is version 0.1 and it shows in places that matter:

- It's young. JAWS has 35 years of fixes for odd applications; NVDA has 20 years and a large
  community finding bugs. Vox has a few months and a beta programme.
- It's mostly the work of one developer. That's a real risk for anyone thinking of relying on
  it at work.
- Its performance hasn't been independently measured yet. The method is written down, but the
  figures haven't been published.
- Firefox support is incomplete: some ARIA roles, landmarks and live regions need a lower-level
  interface (IAccessible2). The pieces that read it are written, but until the helper that
  talks to Firefox is deployed, Vox still reads Firefox through UI Automation.
- Its accessibility conformance report is a self-assessed draft, and its interface
  translations other than English are machine-made and still need review.
- NVDA add-ons don't run in it, and its own add-on ecosystem is close to empty.
- It needs Windows 11. Its magnifier, screen curtain and touch support are new and haven't had
  the use that NVDA's, Narrator's or SuperNova's have; if you rely on magnification all day, a
  dedicated suite is still the safer choice.

**Choose it if** you're an NVDA or JAWS user who's curious, technically comfortable and
happy to report bugs, or you want an open-source reader with modern AI features built in.
Don't make it your only screen reader yet.

## At a glance

| | JAWS | NVDA | Narrator | Vox |
| --- | --- | --- | --- | --- |
| Price | Paid (annual or longer licences) | Free | Free with Windows | Free |
| Source | Closed | Open (GPL) | Closed | Open (MIT) |
| First released | 1989 | 2006 | 2000 | 2026 |
| Windows versions | 10 and 11 | 10 and 11 (64-bit) | Built into Windows | 11 only |
| Web browsing | Excellent | Excellent | Good | Good in Chromium, partial in Firefox |
| Microsoft Office | Excellent | Very good | Improving | Built-in support, young |
| Braille | Yes | Yes | Yes | Yes |
| Extending it | Scripts | Hundreds of add-ons | No | Add-ons, few so far |
| AI descriptions | Built in (cloud) | Through add-ons | Built in (on-device on Copilot+ PCs) | Built in, local or cloud, opt-in |
| Magnifier | With Fusion | Built-in, full-screen only | Windows Magnifier | Built in (docked, lens, full screen), new |
| Support | Paid, phone and email | Community, paid training | Microsoft | Community, GitHub issues |
| Track record | 35 years | 20 years | Built in since 2000 | Months |

## What users wish their screen reader did

Feature lists tell you what a screen reader's makers think matters. To find out what users
think, we read the places they talk: NVDA's issue tracker on GitHub, the AppleVis Windows
forums, the NFB's *Access On* podcast mailbag, WebAIM's 2026 survey, and a 2026 interview study
of how blind users look for help with their computers. The same handful of wishes kept coming
back, whichever screen reader people used.

**"Be fast, and never leave me stranded."** Responsiveness is the complaint JAWS users raise
most often in forum threads: it feels sluggish next to NVDA, especially on modest laptops, and
updates take a long time to install. Narrator users report focus jumping about on busy pages
and commands that work one moment and not the next. Underneath both is the thing every
screen reader user dreads most: a reader that goes silent, leaving you with no way to see
what's wrong.

**"Let me afford it, and let me keep what I paid for."** Cost decides the matter for about a
quarter of NVDA users in WebAIM's survey, against barely one in a hundred JAWS users, whose
licence is usually paid by someone else. Among JAWS users who pay their own way, the
frustrations are less about the headline price than about the terms: upgrades that need a
maintenance agreement, support that depends on a current subscription, and an account
required to use software they've bought. Several asked for something cheaper and more
flexible, such as a monthly plan.

**"Describe what I can't see, and tell me what to do next."** AI is no longer a novelty.
In WebAIM's 2026 survey, six in ten respondents use AI to describe images, half for detailed
descriptions of complex pictures, and four in ten for guidance through an online task. But
the interview study found people want AI help that's built for them: answers in short numbered
steps, no "click the green button", honesty when it doesn't know, and an assistant that
remembers which screen reader and browser they use. Today that help is spread across a
screen reader feature here, an add-on there, and a chatbot in another window.

**"Shut up, but answer when I ask."** People spend their working days in video calls, and
want a reader that stays quiet while they listen and still tells them the time, the window
title or the current line on request. JAWS has a speech-on-demand mode; for NVDA it's one of
the oldest open requests on the tracker (Vox has it too, see below). Related requests: a
single key to mute the microphone in any meeting app, confirmation of cut, copy and paste,
and a quick way to hear or copy the last thing said (NVDA 2026.2 added a key to repeat it).

**"Help me find the command I can't remember."** Screen readers have hundreds of keystrokes,
and nobody remembers them all. NVDA users have asked for a searchable command palette and for
a tutorial on first start. Narrator users complain that their custom shortcuts vanish after
Windows updates. Users of non-English keyboards want AltGr keys to work in shortcuts.

**"Read the web as it's actually written."** Many of the most up-voted NVDA issues are about
web markup that's read wrongly or not at all: definition lists, dialog content spoken twice,
the relationships ARIA describes between controls. Narrator users report heading and link
navigation that works inconsistently in Chrome. When a reader gets the page wrong, the user
usually blames the website.

**"Let my organisation manage it."** IT departments want a "corporate mode": locked
settings, central defaults and no surprise network traffic. That's one of the strongest
reasons JAWS is still the default at work, and an open request on NVDA's tracker.

**"Sound less robotic out of the box."** First impressions still matter. NVDA's default
eSpeak voice is fast and precise but synthetic, Narrator's natural voices win praise, and
people still buy third-party voices such as Eloquence to get the sound they're used to.

### How the readers measure up

No screen reader grants every wish, and none of them could: some wishes pull against each
other, like low cost and phone support. Roughly:

- **JAWS** has speech on demand, deep Office support, corporate deployment and phone support,
  and builds AI in. It falls short on speed, cost and the goodwill lost over its licensing
  changes.
- **NVDA** is free, quick and the most configurable, with the community's add-ons filling many
  gaps. Its tracker shows the open wishes: speech on demand, a command palette, corporate
  mode and built-in image description.
- **Narrator** is free, has good voices and on-device image descriptions on Copilot+ PCs, but
  users find it inconsistent on the web and miss scripting.
- **Vox** was designed around several of these wishes, though being designed for something
  isn't the same as being proven at it:
  - *not stranding you*: crash recovery, "not responding" instead of silence, and a fallback to
    Narrator if it keeps failing;
  - *AI that asks first*: image descriptions, page summaries and questions about a page, with
    a local model or a cloud one, and nothing sent without consent;
  - *finding commands*: command search, input help and practice lessons from the welcome
    wizard;
  - *meetings*: Teams and Zoom support that says when your microphone or camera changes,
    speech on demand so it stays quiet during a call, and one key that mutes the microphone
    in any application;
  - *step-by-step help*: "Guide me" answers "how do I do this on this page?" in short numbered
    steps that name controls by their text, never "the green button", and offers to move you
    to each one;
  - *clipboard*: it says "cut", "copied" or "pasted" with the first few words;
  - *the web as written*: definition lists and `aria-controls` relationships are read;
  - *organisations*: Group Policy, locked settings, a switch to turn off all network access;
  - *what was said*: a speech history and speech viewer;
  - *AltGr*: typing echo handles AltGr characters on international layouts.

  Its speed hasn't been independently measured, its new magnifier, screen curtain and touch
  support are untested by a wide audience, and its web support, while broad, hasn't had years
  of real pages thrown at it.

## Should you switch?

**If you use JAWS and your employer pays for it,** there's little reason to leave unless you
have to. The licence fee is someone else's problem, and JAWS's depth in Office is real. Do keep
NVDA installed as a backup; it costs nothing.

**If you pay for JAWS yourself,** NVDA deserves a serious trial, especially if most of your
work is on the web and in email rather than in complex spreadsheets. Budget a few weeks to
retrain your fingers. If you'd rather keep your JAWS keys, Vox's JAWS keyboard layout is one
way to soften the landing.

**If you use NVDA and you're happy,** stay. Update to 2026.2 when the add-ons you depend on are
compatible, not before.

**If you use NVDA and you're frustrated** by add-on breakage after 2026.1, or you wish image
descriptions and Office support were built in, Vox is worth an afternoon. Its keys are NVDA's,
it imports your settings and dictionaries, and its trial mode puts NVDA back with one key. Just
treat it as a second screen reader for now, not a replacement.

**If you only use Narrator,** try NVDA. You'll probably be surprised how much more you can do,
especially on the web. Keep Narrator for setting up computers.

**If you have some sight,** look at Fusion or SuperNova before any pure screen reader, and keep
an eye on NVDA's magnifier as it matures.

**If you manage screen readers for an organisation,** JAWS is still the safe choice for
heavy Office and line-of-business use. NVDA has become a credible standard for many teams,
and Vox has the deployment features (Group Policy, silent install, settings import) to pilot
alongside them. Its [guide for organisations](organisations.md) includes a cost worksheet.

## The bottom line

There's no wrong answer among the established three. JAWS is the best-supported and most
expensive; NVDA is the best value and, for the web, arguably the best full stop; Narrator is
the one you'll never be without. The interesting change in 2026 is that the gap between them
is increasingly about AI, support and ecosystem, not basic reading.

Vox is the long shot. It's ambitious, it's free, and it was built from the start to make
switching painless. Whether it becomes a real alternative depends on the thing every screen
reader needs most and Vox has least: years of people using it every day and telling it what's
broken. If you want to help with that, the [beta programme](beta.md) is open.

---

*Facts about other screen readers were checked in October 2026 and will date quickly. Prices
are US list prices at the time of writing and vary by country and agreement.*

## Sources

- [WebAIM Screen Reader User Survey #11 results (2026)](https://webaim.org/projects/screenreadersurvey11/)
- [WebAIM Screen Reader User Survey #10 results (2024)](https://webaim.org/projects/screenreadersurvey10/)
- [What's new in JAWS, ZoomText and Fusion 2026 (Freedom Scientific)](https://blog.freedomscientific.com/whats-new-in-jaws-zoomtext-and-fusion-2026/)
- [Picture Smart AI (Freedom Scientific)](https://www.freedomscientific.com/?p=12531)
- [JAWS Home Annual licence (Vispero store)](https://store.vispero.com/products/jaws-home-annual-license)
- [NVDA what's new (NV Access)](https://download.nvaccess.org/documentation/changes.html)
- [NVDA 2026.2 release candidate announcement (NV Access)](https://www.nvaccess.org/?p=33426)
- [NVDA 2026.1 and community add-ons: statistics (nvda-users group)](https://groups.google.com/a/nvaccess.org/g/nvda-users/c/F3Rav4HvBgE)
- [NVDA 64-bit with on-device image description alpha (nvda-users group)](https://groups.google.com/a/nvaccess.org/g/nvda-users/c/Th41cGvjsPI)
- [NonVisual Desktop Access (Wikipedia)](https://en.wikipedia.org/wiki/NonVisual_Desktop_Access)
- [2025: a year in recap, Windows accessibility (Microsoft)](https://blogs.windows.com/windowsexperience/2025/12/03/2025-a-year-in-recap-windows-accessibility/)
- [Narrator image descriptions with Copilot (ittrip)](https://en.ittrip.xyz/windows11/narrator-copilot-images)
- [Upcoming changes to AI features in JAWS and Fusion: age and account requirements (Vispero)](https://training.vispero.com/upcoming-changes-to-ai-features-in-jaws-and-fusion-age-verification-and-account-requirements/)
- [More on changes at Vispero (NFB *Access On* podcast, December 2025)](https://nfb.org/resources/publications-and-media/access-podcast/more-changes-vispero-envision-ally-solos-glasses)
- [JAWS Update thread (AppleVis forum)](https://www.applevis.com/forum/windows/jaws-update)
- [Grudging respect for Narrator, but… (AppleVis forum, May 2025)](https://www.applevis.com/forum/windows/grudging-respect-narrator)
- [NVDA open feature requests (GitHub)](https://github.com/nvaccess/nvda/labels/feature), including
  [speech on demand](https://github.com/nvaccess/nvda/issues/481),
  [mute microphone command](https://github.com/nvaccess/nvda/issues/16056),
  [AltGr in shortcuts](https://github.com/nvaccess/nvda/issues/10767),
  [command palette](https://github.com/nvaccess/nvda/issues/17209),
  [corporate mode](https://github.com/nvaccess/nvda/issues/16599),
  [announce cut, copy and paste](https://github.com/nvaccess/nvda/issues/19042),
  [copy the last phrase](https://github.com/nvaccess/nvda/issues/19385),
  [a tutorial for new users](https://github.com/nvaccess/nvda/issues/7242) and
  [definition lists](https://github.com/nvaccess/nvda/issues/3858)
- [How blind users seek help from forums and generative AI (arXiv, 2026)](https://arxiv.org/abs/2602.18623)
- [Dolphin SuperNova (Double Tap)](https://doubletaponair.com/dolphin-supernova-a-screen-reader-and-magnifier-for-every-stage-of-sight-loss/)
- Vox's own documentation: [switching](switching.md), [Firefox](firefox.md),
  [magnifier and screen curtain](magnifier.md), [touch](touch.md), [AI and Guide me](ai.md),
  [performance](performance.md), [organisations](organisations.md) and the [VPAT draft](vpat.md)
