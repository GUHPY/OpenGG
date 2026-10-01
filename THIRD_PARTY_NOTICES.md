# Source and artwork attribution

OpenGG was extracted from the user's OneRGB project with the project owner's explicit instruction to make a standalone keyboard controller. The imported code, original theme and keyboard artwork are recorded in [the import manifest](docs/import-provenance.json). The Apex preview uses the owner's seven PNG layers exported from their PSD. The PSD and unrelated device art are not included. OpenGG code and the owner-provided keyboard assets use the repository's license ([LICENSE](LICENSE)).

The four original OpenGG logo SVGs were supplied by the owner and remain in the repository. White sidebar typography, PNG logo variants and the multi-size application ICO are derived from those originals with `tools/Build-BrandAssets.ps1`, using native WPF rendering. These owner-provided assets use the repository's license ([LICENSE](LICENSE)).

Lucide icon geometry retains its [ISC license](licenses/Lucide-LICENSE.txt). Preserve that file when redistributing the icons.

The desktop executable uses the Microsoft .NET and Windows Desktop runtimes. Their licenses and notices remain applicable to the runtime files in a self-contained package: [.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [third party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT), [WPF license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT). These projects are separate from OpenGG.

Copies are included in `licenses/` and carried into the portable package. The .NET runtime files came from `Microsoft.NETCore.App.Runtime.win-x64` 10.0.12; the Windows Desktop license/notices came from the installed `Microsoft.NET.Sdk.WindowsDesktop` in SDK 10.0.401. The runtime pack's own Windows Desktop NuGet metadata declares MIT. These upstream notices retain their original wording.

The optional Python CLI uses [cython-hidapi](https://github.com/trezor/cython-hidapi) and [hidapi](https://github.com/libusb/hidapi). The prepared offline tool cache contains their wheel, Python, pip, Frida tools and dependencies. Each wheel retains its original license files in its metadata. These packages have their own license terms; OpenGG's license does not replace them.

The full local portable package also carries unmodified upstream Python, Wireshark, USBPcap and Microsoft Windows SDK installers/layout files. They are separate tools, installed only by an explicit action. Installer license terms and bundled upstream notices remain in those payloads; the SDK is governed by Microsoft's terms. The [tool manifest](tooling/manifest.json) records original download URLs and hashes; [the offline tool guide](tooling/README.md) describes the versions. The installer cache and generated runtime are excluded from the Git source tree, while the preparation and installation scripts are included. USBPcap's corresponding source/symbols archive is retained alongside its installer.

[OmniLED](https://github.com/llMBQll/OmniLED), [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) and earlier Apex investigations are research references. Their application code was not copied into OpenGG. The interface and artwork originate from the owner's OneRGB design.

SteelSeries, Apex, OmniPoint and GG are names of third party products. OpenGG is an independent project. No GG executable, decrypted vendor descriptor, proprietary Lisp source, AES key or firmware image is distributed here.
