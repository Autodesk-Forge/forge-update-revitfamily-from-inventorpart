# Export IPT as SAT files - Design Automation for Inventor

![Platforms](https://img.shields.io/badge/Plugins-Windows-lightgray.svg)
![.NET](https://img.shields.io/badge/.NET%20Framework-4.7-blue.svg)
[![Inventor](https://img.shields.io/badge/Inventor-2019-orange.svg)](http://developer.autodesk.com/)

![Basic](https://img.shields.io/badge/Level-Basic-blue.svg)

# Description

This sample export an `IPT` to `SAT`.

# Setup

## Prerequisites

1. **Visual Studio** 2017
2. **Inventor** 2019 required to compile changes into the plugin
3. **7z zip** requires to create the bundle ZIP, [download here](https://www.7-zip.org/)

## References

This Inventor plugin requires **Inventor.Interop** reference.

## Build

Under **Properties**, at **Build Event** page, the following `Post-build event command line` will copy the DLL into the `\UpdateFamily.bundle/Content/` folder, create a `.ZIP` (using [7z](https://www.7-zip.org/)) and copy to the Webapp folder.

```
xcopy /Y /F "$(TargetDir)*.dll" "$(ProjectDir)ExportSAT.bundle\Contents\"del /F "$(ProjectDir)..\web\wwwroot\bundles\ExportSAT.zip""C:\Program Files\7-Zip\7z.exe" a -tzip "$(ProjectDir)../web/wwwroot/bundles/ExportSAT.zip" "$(ProjectDir)ExportSAT.bundle\" -xr0!*.pdb
```

# Further Reading

- [My First Inventor Plugin](https://knowledge.autodesk.com/support/inventor-products/learn-explore/caas/simplecontent/content/my-first-inventor-plug-overview.html)
- [Inventor Developer Center](https://www.autodesk.com/developer-network/platform-technologies/inventor/overview)

## License

This sample is licensed under the terms of the [MIT License](http://opensource.org/licenses/MIT). Please see the [LICENSE](LICENSE) file for full details.

## Written by

Sajith Subramanian, Forge Partner Development team.