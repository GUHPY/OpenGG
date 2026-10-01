"""Reproduce the initial extraction. Run only against the documented OneRGB snapshot.

Imported files are checked in: this script is research provenance, not a build dependency.
It overwrites the imported files, so do not run it over edited OpenGG sources.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('onergb', type=Path)
args = parser.parse_args()
source = args.onergb.resolve()
root = Path(__file__).resolve().parents[1]
core = root / 'src/OpenGG.Core'
desktop = root / 'src/OpenGG.Desktop'
manifest = []


def copy(path, target, transform=None):
    original = (source / path).read_bytes()
    target.parent.mkdir(parents=True, exist_ok=True)
    if transform:
        target.write_text(transform(original.decode('utf-8-sig').replace('\r\n','\n')), encoding='utf-8', newline='\n')
    else:
        target.write_bytes(original)
    manifest.append({'source': path, 'source_sha256': hashlib.sha256(original).hexdigest(),
                     'destination': target.relative_to(root).as_posix(), 'adapted': transform is not None})


files = ['OneRGB.Application/ControlArbiter.cs', 'OneRGB.Application/DeviceMatching.cs',
         'OneRGB.Application/Devices/DeviceSpecs.cs', 'OneRGB.Application/Presentation/KeyboardLayout.cs',
         'OneRGB.Application/Commands/Shortcuts.cs', 'OneRGB.Application/Commands/VirtualKeys.cs',
         'OneRGB.Application/Lighting/Rgb.cs', 'OneRGB.Application/Lighting/LightingEffects.cs',
         'OneRGB.Application/Lighting/Spatial/LedMaps.cs',
         'OneRGB.Application/Integrations/Integration.cs', 'OneRGB.Application/Integrations/DeviceErrors.cs',
         'OneRGB.Windows/HidEnumerator.cs', 'OneRGB.Windows/Interop/NativeMethods.cs',
         'OneRGB.Hardware/Native/HidWriter.cs', 'OneRGB.Hardware/Native/HidChannel.cs',
         'OneRGB.Hardware/Native/ApexHidLink.cs', 'OneRGB.Hardware/Integrations/ApexKeyboardIntegration.cs']
for folder in ['OneRGB.Domain', 'OneRGB.Application/Control', 'OneRGB.Application/Profiles',
               'OneRGB.Application/Devices/Keyboard', 'OneRGB.Application/Devices/Input']:
    files.extend(p.relative_to(source / 'src').as_posix() for p in (source / 'src' / folder).glob('*.cs')
                 if p.name != 'GameSense.cs')
for file in files:
    copy('src/' + file, core / file.replace('OneRGB.', ''),
         lambda s: s.replace('using OneRGB.Application.Automation;\n', ''))

adapter = (source / 'src/OneRGB.Hardware/Integrations/HidPeripherals.cs').read_text(encoding='utf-8-sig')
adapter = 'using OneRGB.Application;\nusing OneRGB.Application.Control;\nnamespace OneRGB.Hardware.Integrations;\n' + adapter[adapter.index('internal sealed class HidAdapter'):adapter.index('/// <summary>\n/// PRO X2')]
(core / 'Hardware/Integrations/HidAdapter.cs').write_text(adapter, encoding='utf-8')
lighting = (source / 'src/OneRGB.Application/Lighting/Spatial/SpatialLightingEngine.cs').read_text(encoding='utf-8-sig')
(core / 'Application/Lighting/Spatial/IFrameSink.cs').write_text(lighting[:lighting.index('public enum CanvasOutput')], encoding='utf-8')
copy('src/OneRGB.Hardware/Lighting/ApexLighting.cs', core / 'Hardware/Lighting/ApexLighting.cs')


def ui(s):
    return s.replace('OneRGB.Desktop', 'OpenGG.Desktop').replace('/OneRGB;component/', '/OpenGG;component/').replace('assembly=OneRGB.Application', 'assembly=OpenGG.Core')


for file in ['Mvvm/Mvvm.cs', 'Controls/ApexKeyboard.cs', 'Controls/LightingVisualEngine.cs',
             'Controls/Ui.cs', 'Controls/Motion.cs', 'Controls/Icon.cs', 'Controls/Inputs.cs',
             'Controls/ResponsiveGrid.cs', 'Controls/AdaptiveCardFrame.cs', 'Controls/BatteryBar.cs',
             'Controls/DeviceArt.cs', 'Controls/Indicators.cs', 'Controls/ScrollBehavior.cs',
             'ViewModels/KeyboardViewModel.cs', 'ViewModels/DeviceEditorViewModel.cs',
             'Views/Pages/KeyboardPage.xaml', 'Views/Pages/KeyboardPage.xaml.cs',
             'Views/Pages/ApexLightingPage.xaml', 'Views/Pages/ApexLightingPage.xaml.cs',
             'Theme/Tokens.xaml', 'Theme/Icons.xaml']:
    copy('src/OneRGB.Desktop/' + file, desktop / file, ui)
copy('src/OneRGB.Desktop/app.manifest', desktop / 'app.manifest')
for file in (source / 'src/OneRGB.Desktop/Assets/Devices/Apex').glob('*.png'):
    copy(file.relative_to(source).as_posix(), desktop / 'Assets/Devices/Apex' / file.name)
for file in ['Assets/Devices/keyboard.png', 'Assets/Lucide-LICENSE.txt']:
    copy('src/OneRGB.Desktop/' + file, desktop / file)

# Keep the actual OneRGB resource definitions, removing unrelated overlays and settings editors.
ns = {'w': 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
xkey = '{http://schemas.microsoft.com/winfx/2006/xaml}Key'
for filename in ['Controls.xaml', 'Templates.xaml']:
    content = ui((source / 'src/OneRGB.Desktop/Theme' / filename).read_text(encoding='utf-8-sig'))
    tree = ET.fromstring(content)
    for node in list(tree):
        xml = ET.tostring(node, encoding='unicode')
        if filename == 'Templates.xaml':
            if node.tag.endswith('MergedDictionaries') or node.get(xkey) in ['T.Brush','T.Visible','T.Glow','T.Res','DeviceTile','DeviceHero','WriteState','Conflict','PageScroll']:
                continue
            tree.remove(node)
        elif any('ctl:' + kind in xml for kind in ['ToastView','ConfirmDialog','Sheet','StatusBadge']):
            tree.remove(node)
    ET.register_namespace('', ns['w'])
    ET.register_namespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
    # Prefixes referenced inside markup extensions must also remain declared.
    rendered = ET.tostring(tree, encoding='unicode').replace('<ResourceDictionary ', '<ResourceDictionary xmlns:ctl="clr-namespace:OpenGG.Desktop.Controls" xmlns:vm="clr-namespace:OpenGG.Desktop.ViewModels" xmlns:mvvm="clr-namespace:OpenGG.Desktop.Mvvm" ', 1)
    (desktop / 'Theme' / filename).write_text(rendered, encoding='utf-8')

(root / 'docs').mkdir(exist_ok=True)
(root / 'docs/import-provenance.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(f'Extracted {len(manifest)} sources and assets. Next: apply the documented OpenGG host adaptations.')
