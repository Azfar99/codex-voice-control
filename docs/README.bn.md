# CODEX Voice Control 0.4.1 — বাংলা নির্দেশনা

Windows-এ কথা বলে Codex-এর নিজস্ব dictation চালানোর ছোট companion। পুরো বাংলা–English লেখা Codex নিজেই transcribe করে; এখানে আলাদা Whisper বা message transcription model নেই।

## ইনস্টল ও ব্যবহার

1. নিজের account দিয়ে compatible OpenAI desktop app-এ sign in করো; Codex mode ও Dictate থাকতে হবে।
2. Setup EXE চালাও, তারপর desktop-এর **CODEX Voice Control** icon-এ double-click করো। Portable ZIP হলে extract করে `CODEX.exe` চালাও। সাধারণ ব্যবহার ফ্রি; password বা license key লাগে না।
3. `Hello Codex`, `Okay Codex`, `Hey Codex` বলে ডাকো। Recording চালু হলে স্বাভাবিকভাবে কথা বলো।
4. অন্তত **২ সেকেন্ড চুপ থেকে**, আলাদা করে `Stop` / `থামো` বললে recording শেষ হবে; তীরচিহ্নে Codex-এর আসল draft দেখা যাবে।
5. আবার `Start` / `Okay start` বলে আরও কথা যোগ করা যায়।
6. অন্তত **২ সেকেন্ড চুপ থেকে** `Send` / `Run` বললে পাঠাবে। Recording চললেও সরাসরি Send বলা যায়—নিজে Stop করে transcription প্রস্তুত হওয়ার অপেক্ষা করে পাঠাবে।

কথার মাঝখানে stop/send/run থাকলে কমান্ড ধরে না; শব্দের পর আবার কথা চালালেও কমান্ড আটকানোর পরীক্ষা আছে। Direct Send-এর ক্ষেত্রে native লেখার শেষেও Send/Run নিশ্চিত না হলে review করার জন্য draft রেখে দেয়।

বাংলা wake বিকল্প: `হ্যালো কোডেক্স`, `ওকে কোডেক্স`, `কোডেক্স আসো`, `কোডেক্স আছো`, `কোডেক্স কি তুমি আছো`, `আচ্ছা কোডেক্স শোনো`। ছোট command-এর জন্য Windows English recognizer-এ বাংলা pronunciation আছে; সবার accent-এ একই accuracy নিশ্চিত নয়। পুরো কথার transcription Codex-এর নিজের।

## ছোট control ও বড় উইন্ডো

- Idle-এ অন্য উইন্ডোর পেছনে থাকে, active session-এ শুধু ছোট control সামনে আসে।
- Idle/চুপ থাকলে সবুজ; কথা বললে frame, waves ও particles হলুদ হয়ে বেশি কাঁপে। Default size ৭৬ px; drag করে সরানো ও right-click-এ আকার বদলানো যায়।
- Orb-এ click = Record/Stop; তীরচিহ্ন = preview। Hide করলে tray ও microphone চালু থাকে।
- **Stop completely (until I open it again)** দিলে microphone ও process পুরো বন্ধ এবং login startup বন্ধ হয়। Desktop icon-এ double-click করলে আবার চালু হবে; startup ফের চাইলে menu-তে আলাদা করে on করো।
- `Ctrl+Alt+D` = manual Record/Stop; `Ctrl+Alt+Escape` = mic pause, pending Send বাতিল এবং নিজের শুরু করা recording cancel করার চেষ্টা।

Codex-এর বড় উইন্ডো আড়াল রেখেই companion তার Record ও Stop বাটন চালায়। Codex ফোকাস নিলে আগের অ্যাপে ফোকাস ফেরানো হয়। পরীক্ষায় minimized ও পেছনে খোলা দুই অবস্থাতেই বড় উইন্ডো দেখা যায়নি। Codex-এ একটি chat খুলে minimized করে রাখো। পুরোপুরি Quit করলে আবার হাতে খুলতে হবে। পাঠানো কাজ শেষের নোটিফিকেশনের জন্য Codex-এর **Settings > Notifications** ব্যবহার করো।

**শেষের Stop/Send শব্দ মুছতে আর Codex editor-এর focus নেওয়া হয় না।** ফলে Codex শব্দটি লিখে ফেললে সেটি draft/message-এর শেষে থাকতে পারে। Preview-তে আসল লেখাটিই দেখা যাবে।

## কী লাগে ও কী জমা হয়

Windows 10 1809+/11 x64, .NET Framework 4.7.2+, compatible Codex app ও নিজের account লাগে। ছোট voice command-এর জন্য Windows English (United States) speech recognition চাই। না থাকলে Record button ব্যবহার করা যায়। Python, downloadable model বা API key লাগে না। অন্য কারও login/account এর সঙ্গে share হয় না।

`data/`-তে preferences ও সীমিত আকারের command/error/timing logs থাকে; companion microphone recording বা message transcript disk-এ রাখে না। Codex-এর নিজস্ব data/network ব্যবহার তার settings ও terms অনুযায়ী চলে। RAM কয়েক KB নয়—companion-এর MiB লাগে এবং Codex app-এর RAM আলাদা।

একই PC-র পরীক্ষায় preview read median ৪৮ ms থেকে ২ ms হয়েছে। Preview প্রতি ২৫০ ms-এ আপডেট হয়। Codex-এর transcription-এর সময় নেটওয়ার্ক ও সেবার ওপর নির্ভর করে। ০.৪.১-এ পেছনে থাকা Codex-এর Record/Stop, command audio ও ইনস্টলার পরীক্ষা পাস করেছে। অন্য PC ও ভবিষ্যৎ Codex সংস্করণে আচরণ বদলাতে পারে। Setup unsigned।

Download/use/নিজের copy বদলানো এবং free share করা ফ্রি, copyright credit রাখতে হবে। Software বা paid software service হিসেবে বিক্রির জন্য আলাদা লিখিত চুক্তি লাগে; স্বয়ংক্রিয় টাকা/royalty কাটা হয় না। [পুরো license](../LICENSE.txt) · [English guide](../README.md)।
