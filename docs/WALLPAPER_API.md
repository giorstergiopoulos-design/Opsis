# Opsis web wallpapers — API for authors (2.2.0)

A **Page** wallpaper is an ordinary web page (a local `index.html`, a folder with one, a `.zip` package, a website or a YouTube link). Only **local** pages (`file://`) get the Opsis API below; websites and YouTube are just shown.

## 1. Folder / package
```
my-wallpaper/
  index.html
  opsis-wallpaper.json      (optional: name + the settings Opsis shows for it)
  anything else (js, css, images, video)
```
A `.zip` of that folder is a **package**: Wallpaper page → Page mode → *Import package (.zip)* extracts it to `%AppData%\Opsis\wallpapers\<name>` and uses it; *Export package…* does the reverse. (Imports refuse paths that escape the folder, more than 4000 files or 400 MB.) A working example ships with Opsis: `wallpapers\samples\system-monitor` (pick it with *Use sample*).

## 2. Messages Opsis sends to the page
Everything arrives with `window.addEventListener('message', e => …)`; `e.data.opsis === true`.

| `type` | payload | when |
|---|---|---|
| `hello` | `version` | the page has loaded — now you may subscribe |
| `props` | `props: { key: value }` | on load and whenever the user changes a setting (sliders/numbers are numbers, check boxes booleans, the rest strings) |
| `system` | `data` (below) | once per second |
| `audio` | `bands: number[24]` (0..1) | ~12 times per second, only after you subscribed |

`data` of `system`: `cpu` %, `ramUsedPercent`, `ramTotalMb`, `ramAvailableMb`, `netDownKbps`, `netUpKbps`, `processes`, `batteryPercent` (-1 = no battery), `charging`, `disks[] {name,totalMb,freeMb,usedPercent}`, `gpu {name,load,temperature,memoryUsedMb}` (only after subscribing with `gpu:true`, otherwise null).

Subscribe from the page: `parent.postMessage({ opsis: 'subscribe', audio: true, gpu: false }, '*')`.

Nothing is sent while the wallpaper is paused (fullscreen app, lock, battery saver, load limits…), and the page itself is frozen after 15 s of pause.

## 3. `opsis-wallpaper.json`
```json
{ "name": "My wallpaper", "author": "me", "description": "…",
  "properties": [
    { "key": "accent", "type": "color",    "label": "Accent", "labelEl": "Χρώμα", "default": "#00c8e6" },
    { "key": "speed",  "type": "slider",   "label": "Speed",  "min": 1, "max": 10, "step": 1, "default": 5 },
    { "key": "clock",  "type": "checkbox", "label": "Clock",  "default": true },
    { "key": "style",  "type": "combo",    "label": "Style",  "options": ["Bars","Line"], "default": "Bars" },
    { "key": "title",  "type": "text",     "label": "Title",  "default": "OPSIS" } ] }
```
Opsis builds the controls under the page chooser and stores the values per wallpaper.

## 4. Wallpaper elements (any mode)
Wallpaper page → *Wallpaper elements*: analog clock, CPU, memory, GPU, network, disks, battery, audio spectrum — the pieces of the Opsis widgets, drawn over **every** mode (waves, video, image, page…). The elements form one column on the LEFT or the RIGHT edge of the screen (the user chooses the side and Top/Middle/Bottom), each with its own size, plus a frosted card and an accent colour. Code: `src/Widgets/wallpaper/elements.js`; settings: `WallpaperSettings.Elements`.

## 5. Safety model
The page runs in an iframe of the wallpaper page; it cannot reach Opsis host objects (cross-origin). Remote pages get no data at all. Only `file://` pages are connected to the API.
