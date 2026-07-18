# AppData Local Deployment for Debug Builds

To resolve local write permission blockages when compiling without administrative privileges, we deploy debug `.addin` manifest files directly to the user-specific `%APPDATA%\Autodesk\Revit\Addins\<Year>\` directory. Release builds continue to target `%PROGRAMDATA%` for machine-wide installer staging.
