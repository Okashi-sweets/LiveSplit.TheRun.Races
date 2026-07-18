# Third-party notices

This project interoperates with LiveSplit and therun.gg and contains code
adapted from the official therun.gg LiveSplit component. The notices below
must be retained in source and binary distributions containing substantial
portions of that code.

## LiveSplit.TheRun

Source: <https://github.com/therungg/LiveSplit.TheRun>

MIT License

Copyright (c) 2024 therun.gg

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## LiveSplit

Source: <https://github.com/LiveSplit/LiveSplit>

MIT License. Copyright (c) 2013 Christopher Serr and Sergey Papushin.
The full license is available in the linked upstream repository.

## therun frontend

Source: <https://github.com/therungg/therun-frontend>

The frontend was consulted to identify race-room behavior and API contracts.
No frontend source code, styles, images, or other assets are bundled with this
project. The checked-out frontend source did not contain a root license file,
so this notice does not claim or rely on a license grant for that repository.

## LiveSplit.Racetime

Source: <https://github.com/LiveSplit/LiveSplit.Racetime>

The repository did not contain an explicit license file when this project was
prepared. It was consulted to understand LiveSplit's race-provider integration
and window behavior. No LiveSplit.Racetime source file is bundled or claimed
as relicensed by this project.

## Microsoft WebView2

This project references the `Microsoft.Web.WebView2` NuGet package. Its own
license permits source and binary redistribution subject to retaining its
copyright, conditions, and disclaimer. Its full `LICENSE.txt` and `NOTICE.txt`
are distributed with the NuGet package. This project's release artifact does
not redistribute WebView2 binaries; it uses the compatible assemblies already
bundled with LiveSplit for Racetime. If a distributor chooses to bundle those
binaries separately, the Microsoft package license and notices must accompany
that distribution.
