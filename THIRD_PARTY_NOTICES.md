# Third-party notices

## DesktopFences ("Desktop Fences +")

Opsis 2.2.0 bundles the Desktop Fences + source code (https://github.com/limbo666/DesktopFences, WPF, version 2.6.6.234)
under `fences/` as the separate companion app `OpsisFences.exe`, which provides the DeskContainers. Changes made for Opsis:
retargeted to .NET 10, the COM shortcut interop replaced by a late-bound shim (`WshShim.cs`), all data moved to
`%AppData%Opsisences` (`DataRoot.cs`), the upstream update/news check disabled, visible product name changed to "Opsis Fences",
and the process exits together with Opsis. The earlier WinForms re-implementation of some of its features is still used when
the engine is switched off.

> MIT License
>
> Copyright (c) 2025 LIMBO666
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.
>
> This project includes code originally developed by HakanKokcu as part of the BirdyFences
> project, which was licensed under the MIT License at the time of this project's creation.
