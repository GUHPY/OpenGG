# OpenGG documentation

| Read this | To do this |
|---|---|
| [Getting started](getting-started.md) | Run the desktop, choose a slot, adjust keys and save |
| [Visual editor guide](interface.md) | Use Color/OLED, image conversion, key painting, vertical sliders and the shared tools card |
| [Protocol](protocol.md) | Implement Windows HID requests, ACKs, live batches and flash |
| [Profile format](profile-format.md) | Understand schema 19 offsets, CRC, thresholds and key indexes |
| [Protocol data](protocol-data.json) | Use exact tables and mappings from another implementation |
| [Diagnostics](diagnostics.md) | Identify a keyboard, test communication and use research tools |
| [Offline research tools](../tooling/README.md) | Install included local payloads, prepare Python/Frida and verify their hashes |
| [Reproduce the investigation](reproduce.md) | Run the CLI, reconstruct captures and perform checks |
| [Research log](research-log.md) | Follow every investigation stage, correction and implementation decision |
| [Architecture](architecture.md) | Understand the imported code and the control/transport pipeline |
| [OneRGB integration](onergb-integration.md) | Follow the Apex backport, reused canvas and original-project checks |
| [Verification](verification.md) | Separate captured, acknowledged, stored and physically observed behavior |
| [Troubleshooting](troubleshooting.md) | Resolve ownership, timeouts, profile and OLED issues |
| [Sources](sources.md) | Find primary API and independent protocol references |
| [Import manifest](import-provenance.json) | Trace imported source files and the owner's artwork |
| [Input manifest](input-manifest.json) | Identify original research inputs by size and SHA256 |

The maintained docs are English. Historical originals remain in their original language in the local archive. Raw captures, full profiles and the transcript are deliberately absent from the public source tree; they can contain unrelated traffic or personal configuration. This does not remove the protocol findings: public docs contain the byte layout, equations, lookup tables, commands, checks, results and evidence boundaries needed to reproduce the work.
