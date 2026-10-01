# Sources and attribution

The decisive evidence is the owner's capture, the complete-file reconstruction and actual receiver responses documented in [verification](verification.md). Earlier protocol documents are historical inputs; conclusions superseded by this hardware investigation are listed in the research log.

Primary API references:

- [Microsoft: HID client overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/)
- [Microsoft: HidD_GetFeature](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getfeature)
- [Microsoft: HidD_SetFeature](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setfeature)
- [Microsoft: DEVPKEY_Device_ContainerId](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-containerid)
- [Microsoft: SetupDiGetDevicePropertyW](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetdevicepropertyw)
- [Microsoft SDK property definitions](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/devpkey.h): Container ID property namespace `8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C`, property ID 2, GUID type
- [Microsoft: USB event capture with Logman](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/how-to-capture-a-usb-event-trace)
- [Microsoft: USBView](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/usbview)

Independent implementation/research references:

- [OmniLED device configuration](https://github.com/llMBQll/OmniLED/blob/main/config/devices.lua) and [renderer buffer](https://github.com/llMBQll/OmniLED/blob/main/omni-led-lib/src/renderer/buffer.rs): OLED geometry/page layout cross-check
- [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) and its [Gen 3 wireless RGB merge request](https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/3143): earlier public RGB work on receivers `1644` and cable `1646`; advanced-write support was not inferred from RGB support
- [apex-web](https://github.com/trottyva/apex-web): older 2023-model findings, useful comparison rather than Gen 3 proof
- [USBPcap](https://github.com/desowin/usbpcap), [Wireshark documentation](https://www.wireshark.org/docs/wsug_html_chunked/) and [Frida documentation](https://frida.re/docs/home/): optional investigation tools
- [cython-hidapi](https://github.com/trezor/cython-hidapi) / [hidapi](https://github.com/libusb/hidapi): optional Python hardware transport

The OLED precision observation comes from the owner's comparison of the native wheel menu, GG and OpenGG during this session. It is not attributed to an official spec that states a measured physical depth. The vendor's installed metadata was inspected locally for this device's tables/schema; no proprietary source or decryption material is redistributed.

References were checked on 1 October 2026. OpenGG records original advanced Gen 3 research, without a blanket first-ever claim: the linked public RGB work and OmniLED's Apex OLED implementation predate this repository. The interface design, keyboard artwork and four OpenGG logos are the owner's original work.
