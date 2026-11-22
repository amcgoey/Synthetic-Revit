### File: Revit API Synthetic v2/src/Synthetic2022/packages.config
```xml
﻿<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Microsoft.Office.Interop.Excel" version="15.0.4795.1001" targetFramework="net48" />
  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
  <package id="Revit_All_Main_Versions_API_x64" version="2022.1.0" targetFramework="net48" />
</packages>
```

### File: Revit API Synthetic v2/src/Synthetic2022/Synthetic2022.addin
```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Synthetic</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Synthetic2022.dll</Assembly>
    <FullClassName>Synthetic.Core.App</FullClassName>
    <ClientId>4fd5b26e-1325-460f-97b1-878804070868</ClientId>
    <VendorId>net.amcgoey</VendorId>
    <VendorDescription>Synthetic by Arthur McGoey</VendorDescription>
  </AddIn>
</RevitAddIns>
```

### File: Revit API Synthetic v2/src/Synthetic2022/Synthetic2022.csproj
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="14.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch>
      None
    </ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch>
    <TargetFrameworkProfile />
  </PropertyGroup>
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">x64</Platform>
    <ProjectGuid>{09C5C323-AB25-489C-9E21-D307F2D9B1A4}</ProjectGuid>
    <OutputType>Library</OutputType>
    <ProjectTypeGuids>{60dc8134-eba5-43b8-bcc9-bb4bc16c2548};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <AppDesignerFolder>Properties</AppDesignerFolder>
    <RootNamespace>Synthetic</RootNamespace>
    <AssemblyName>Synthetic2022</AssemblyName>
    <RevitVersion>2022</RevitVersion>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <FileAlignment>512</FileAlignment>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <DefineConstants>TRACE;DEBUG;REVIT2022</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <StartAction>Program</StartAction>
    <StartProgram>$(ProgramW6432)\Autodesk\Revit $(RevitVersion)\Revit.exe</StartProgram>
    <DocumentationFile>..\..\output\Synthetic\Synthetic2022.xml</DocumentationFile>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <DefineConstants>TRACE;REVIT2022</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <StartAction>Program</StartAction>
    <StartProgram>$(ProgramW6432)\Autodesk\Revit $(RevitVersion)\Revit.exe</StartProgram>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="AdWindows, Version=3.1.7.0, Culture=neutral, processorArchitecture=MSIL">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2022.1.0\lib\net48\AdWindows.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="eTransmitForRevitDB">
      <HintPath>C:\Program Files\Autodesk\eTransmit for Revit 2022\eTransmitForRevitDB.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="Microsoft.CSharp" />
    <Reference Include="Microsoft.Office.Interop.Excel, Version=15.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c, processorArchitecture=MSIL">
      <HintPath>..\packages\Microsoft.Office.Interop.Excel.15.0.4795.1001\lib\net20\Microsoft.Office.Interop.Excel.dll</HintPath>
      <EmbedInteropTypes>True</EmbedInteropTypes>
    </Reference>
    <Reference Include="Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed, processorArchitecture=MSIL">
      <HintPath>..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll</HintPath>
    </Reference>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="System.Xaml" />
    <Reference Include="WindowsBase" />
    <Reference Include="RevitAPI, Version=22.0.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2022.1.0\lib\net48\RevitAPI.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="RevitAPIUI, Version=22.0.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2022.1.0\lib\net48\RevitAPIUI.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="System" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="UIFramework, Version=22.0.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2022.1.0\lib\net48\UIFramework.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="Synthetic2022.addin">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <ItemGroup>
    <None Include="packages.config" />
  </ItemGroup>

  <Import Project="..\SyntheticShared\SyntheticShared.projitems" Label="Shared" />
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />

  <!-- Define the Inline C# Task to handle Regex replacement -->
  <UsingTask TaskName="ReplaceAddinPath" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
    <ParameterGroup>
      <InputFile ParameterType="System.String" Required="true" />
      <OutputFile ParameterType="System.String" Required="true" />
      <NewPath ParameterType="System.String" Required="true" />
    </ParameterGroup>
    <Task>
      <Using Namespace="System" />
      <Using Namespace="System.IO" />
      <Using Namespace="System.Text.RegularExpressions" />
      <Code Type="Fragment"><![CDATA[
        try {
            string content = File.ReadAllText(InputFile);
            string pattern = "(?i)<Assembly>.*?</Assembly>";
            string replacement = "<Assembly>" + NewPath.Replace("$", "$$") + "</Assembly>";
            string newContent = Regex.Replace(content, pattern, replacement);
            File.WriteAllText(OutputFile, newContent);
        } catch (Exception ex) {
            Log.LogError("Error replacing addin path: " + ex.Message);
            return false;
        }
      ]]></Code>
    </Task>
  </UsingTask>

  <!-- Deploy and update the .addin file for both Debug and Release configurations -->
  <Target Name="DeployAddin" AfterTargets="Build" Condition="Exists('$(ProjectDir)$(AssemblyName).addin')">
    <PropertyGroup>
      <SourceAddinFile>$(ProjectDir)$(AssemblyName).addin</SourceAddinFile>
      <LocalOutputAddin>$(TargetDir)$(AssemblyName).addin</LocalOutputAddin>
      <!-- Determine correct Assembly Path based on Configuration -->
      <NewAssemblyPath Condition="'$(Configuration)' == 'Debug'">$(TargetPath)</NewAssemblyPath>
      <NewAssemblyPath Condition="'$(Configuration)' != 'Debug'">C:\ProgramData\Autodesk\Revit\Addins\Synthetic\$(AssemblyName).dll</NewAssemblyPath>
    </PropertyGroup>

    <Message Text="Replacing assembly path in $(Configuration) .addin: $(LocalOutputAddin) -> $(NewAssemblyPath)" Importance="high" />
    
    <!-- Execute the C# Inline Task -->
    <ReplaceAddinPath InputFile="$(SourceAddinFile)" OutputFile="$(LocalOutputAddin)" NewPath="$(NewAssemblyPath)" />

    <!-- Copy the Debug manifest directly to AppData Revit Addins folder -->
    <Copy SourceFiles="$(LocalOutputAddin)" DestinationFolder="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\" Condition="'$(Configuration)' == 'Debug'" />
  </Target>

  <Target Name="AfterClean">
    <Delete Files="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\$(AssemblyName).addin" />
  </Target>

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>
</Project>
```

### File: Revit API Synthetic v2/src/Synthetic2023/App.config
```xml
﻿<?xml version="1.0" encoding="utf-8" ?>
<configuration>
    <startup> 
        <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
    </startup>
</configuration>
```

### File: Revit API Synthetic v2/src/Synthetic2023/packages.config
```xml
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Microsoft.Office.Interop.Excel" version="15.0.4795.1001" targetFramework="net48" />
  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
  <package id="Revit_All_Main_Versions_API_x64" version="2023.0.0" targetFramework="net48" />
</packages>
```

### File: Revit API Synthetic v2/src/Synthetic2023/Synthetic2023.addin
```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Synthetic</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Synthetic2023.dll</Assembly>
    <FullClassName>Synthetic.Core.App</FullClassName>
    <ClientId>4fd5b26e-1325-460f-97b1-878804070868</ClientId>
    <VendorId>net.amcgoey</VendorId>
    <VendorDescription>Synthetic by Arthur McGoey</VendorDescription>
  </AddIn>
</RevitAddIns>
```

### File: Revit API Synthetic v2/src/Synthetic2023/Synthetic2023.csproj
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">x64</Platform>
    <ProjectGuid>{2297FF48-4170-498C-A008-7E8DE1150590}</ProjectGuid>
    <OutputType>Library</OutputType>
    <ProjectTypeGuids>{60dc8134-eba5-43b8-bcc9-bb4bc16c2548};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <RootNamespace>Synthetic</RootNamespace>
    <AssemblyName>Synthetic2023</AssemblyName>
    <RevitVersion>2023</RevitVersion>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <FileAlignment>512</FileAlignment>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <DefineConstants>TRACE;DEBUG;REVIT2023</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <StartAction>Program</StartAction>
    <StartProgram>$(ProgramW6432)\Autodesk\Revit $(RevitVersion)\Revit.exe</StartProgram>
    <DocumentationFile>..\..\output\Synthetic\Synthetic2023.xml</DocumentationFile>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <DefineConstants>TRACE;REVIT2023</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <StartAction>Program</StartAction>
    <StartProgram>$(ProgramW6432)\Autodesk\Revit $(RevitVersion)\Revit.exe</StartProgram>
  </PropertyGroup>
  <PropertyGroup>
    <StartupObject />
  </PropertyGroup>
  <ItemGroup>
    <None Include="App.config" />
    <None Include="packages.config" />
    <Content Include="Synthetic2023.addin">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <ItemGroup>
    <Reference Include="AdWindows, Version=4.0.0.6, Culture=neutral, processorArchitecture=MSIL">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2023.0.0\lib\net48\AdWindows.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="eTransmitForRevit">
      <HintPath>C:\Program Files\Autodesk\eTransmit for Revit 2023\eTransmitForRevit.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="eTransmitForRevitDB">
      <HintPath>C:\Program Files\Autodesk\eTransmit for Revit 2023\eTransmitForRevitDB.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="Microsoft.CSharp" />
    <Reference Include="Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed, processorArchitecture=MSIL">
      <HintPath>..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll</HintPath>
    </Reference>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="System.Xaml" />
    <Reference Include="WindowsBase" />
    <Reference Include="RevitAPI, Version=23.0.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2023.0.0\lib\net48\RevitAPI.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="RevitAPIUI, Version=23.0.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2023.0.0\lib\net48\RevitAPIUI.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="System" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="UIFramework, Version=23.0.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2023.0.0\lib\net48\UIFramework.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Microsoft.Office.Interop.Excel, Version=15.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c, processorArchitecture=MSIL">
      <HintPath>..\packages\Microsoft.Office.Interop.Excel.15.0.4795.1001\lib\net20\Microsoft.Office.Interop.Excel.dll</HintPath>
      <EmbedInteropTypes>True</EmbedInteropTypes>
    </Reference>
  </ItemGroup>

  <Import Project="..\SyntheticShared\SyntheticShared.projitems" Label="Shared" />
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />

  <!-- Define the Inline C# Task to handle Regex replacement -->
  <UsingTask TaskName="ReplaceAddinPath" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
    <ParameterGroup>
      <InputFile ParameterType="System.String" Required="true" />
      <OutputFile ParameterType="System.String" Required="true" />
      <NewPath ParameterType="System.String" Required="true" />
    </ParameterGroup>
    <Task>
      <Using Namespace="System" />
      <Using Namespace="System.IO" />
      <Using Namespace="System.Text.RegularExpressions" />
      <Code Type="Fragment"><![CDATA[
        try {
            string content = File.ReadAllText(InputFile);
            string pattern = "(?i)<Assembly>.*?</Assembly>";
            string replacement = "<Assembly>" + NewPath.Replace("$", "$$") + "</Assembly>";
            string newContent = Regex.Replace(content, pattern, replacement);
            File.WriteAllText(OutputFile, newContent);
        } catch (Exception ex) {
            Log.LogError("Error replacing addin path: " + ex.Message);
            return false;
        }
      ]]></Code>
    </Task>
  </UsingTask>

  <!-- Deploy and update the .addin file for both Debug and Release configurations -->
  <Target Name="DeployAddin" AfterTargets="Build" Condition="Exists('$(ProjectDir)$(AssemblyName).addin')">
    <PropertyGroup>
      <SourceAddinFile>$(ProjectDir)$(AssemblyName).addin</SourceAddinFile>
      <LocalOutputAddin>$(TargetDir)$(AssemblyName).addin</LocalOutputAddin>
      <!-- Determine correct Assembly Path based on Configuration -->
      <NewAssemblyPath Condition="'$(Configuration)' == 'Debug'">$(TargetPath)</NewAssemblyPath>
      <NewAssemblyPath Condition="'$(Configuration)' != 'Debug'">C:\ProgramData\Autodesk\Revit\Addins\Synthetic\$(AssemblyName).dll</NewAssemblyPath>
    </PropertyGroup>

    <Message Text="Replacing assembly path in $(Configuration) .addin: $(LocalOutputAddin) -> $(NewAssemblyPath)" Importance="high" />
    
    <!-- Execute the C# Inline Task -->
    <ReplaceAddinPath InputFile="$(SourceAddinFile)" OutputFile="$(LocalOutputAddin)" NewPath="$(NewAssemblyPath)" />

    <!-- Copy the Debug manifest directly to AppData Revit Addins folder -->
    <Copy SourceFiles="$(LocalOutputAddin)" DestinationFolder="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\" Condition="'$(Configuration)' == 'Debug'" />
  </Target>

  <Target Name="AfterClean">
    <Delete Files="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\$(AssemblyName).addin" />
  </Target>

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>
</Project>
```

### File: Revit API Synthetic v2/src/Synthetic2023/Synthetic2023_v20gsudp_wpftmp.csproj
```xml
﻿<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">x64</Platform>
    <ProjectGuid>{2297FF48-4170-498C-A008-7E8DE1150590}</ProjectGuid>
    <OutputType>Library</OutputType>
    <ProjectTypeGuids>{60dc8134-eba5-43b8-bcc9-bb4bc16c2548};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <RootNamespace>Synthetic</RootNamespace>
    <AssemblyName>Synthetic2023</AssemblyName>
    <RevitVersion>2023</RevitVersion>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <FileAlignment>512</FileAlignment>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <DefineConstants>TRACE;DEBUG;REVIT2023</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <StartAction>Program</StartAction>
    <StartProgram>$(ProgramW6432)\Autodesk\Revit $(RevitVersion)\Revit.exe</StartProgram>
    <DocumentationFile>..\..\output\Synthetic\Synthetic2023.xml</DocumentationFile>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <DefineConstants>TRACE;REVIT2023</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <StartAction>Program</StartAction>
    <StartProgram>$(ProgramW6432)\Autodesk\Revit $(RevitVersion)\Revit.exe</StartProgram>
  </PropertyGroup>
  <PropertyGroup>
    <StartupObject />
  </PropertyGroup>
  <ItemGroup>
    <None Include="App.config" />
    <None Include="packages.config" />
    <Content Include="Synthetic2023.addin">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <ItemGroup>
  </ItemGroup>
  <ItemGroup>
  </ItemGroup>
  <Import Project="..\SyntheticShared\SyntheticShared.projitems" Label="Shared" />
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
  <!-- Define the Inline C# Task to handle Regex replacement -->
  <UsingTask TaskName="ReplaceAddinPath" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
    <ParameterGroup>
      <InputFile ParameterType="System.String" Required="true" />
      <OutputFile ParameterType="System.String" Required="true" />
      <NewPath ParameterType="System.String" Required="true" />
    </ParameterGroup>
    <Task>
      <Using Namespace="System" />
      <Using Namespace="System.IO" />
      <Using Namespace="System.Text.RegularExpressions" />
      <Code Type="Fragment"><![CDATA[
        try {
            string content = File.ReadAllText(InputFile);
            string pattern = "(?i)<Assembly>.*?</Assembly>";
            string replacement = "<Assembly>" + NewPath.Replace("$", "$$") + "</Assembly>";
            string newContent = Regex.Replace(content, pattern, replacement);
            File.WriteAllText(OutputFile, newContent);
        } catch (Exception ex) {
            Log.LogError("Error replacing addin path: " + ex.Message);
            return false;
        }
      ]]></Code>
    </Task>
  </UsingTask>
  <!-- Deploy and update the .addin file for both Debug and Release configurations -->
  <Target Name="DeployAddin" AfterTargets="Build" Condition="Exists('$(ProjectDir)$(AssemblyName).addin')">
    <PropertyGroup>
      <SourceAddinFile>$(ProjectDir)$(AssemblyName).addin</SourceAddinFile>
      <LocalOutputAddin>$(TargetDir)$(AssemblyName).addin</LocalOutputAddin>
      <!-- Determine correct Assembly Path based on Configuration -->
      <NewAssemblyPath Condition="'$(Configuration)' == 'Debug'">$(TargetPath)</NewAssemblyPath>
      <NewAssemblyPath Condition="'$(Configuration)' != 'Debug'">C:\ProgramData\Autodesk\Revit\Addins\Synthetic\$(AssemblyName).dll</NewAssemblyPath>
    </PropertyGroup>
    <Message Text="Replacing assembly path in $(Configuration) .addin: $(LocalOutputAddin) -&gt; $(NewAssemblyPath)" Importance="high" />
    <!-- Execute the C# Inline Task -->
    <ReplaceAddinPath InputFile="$(SourceAddinFile)" OutputFile="$(LocalOutputAddin)" NewPath="$(NewAssemblyPath)" />
    <!-- Copy the Debug manifest directly to AppData Revit Addins folder -->
    <Copy SourceFiles="$(LocalOutputAddin)" DestinationFolder="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\" Condition="'$(Configuration)' == 'Debug'" />
  </Target>
  <Target Name="AfterClean">
    <Delete Files="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\$(AssemblyName).addin" />
  </Target>
  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>
    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>
  <ItemGroup>
    <ReferencePath Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\packages\Revit_All_Main_Versions_API_x64.2023.0.0\lib\net48\AdWindows.dll" />
    <ReferencePath Include="C:\Program Files\Autodesk\eTransmit for Revit 2023\eTransmitForRevit.dll" />
    <ReferencePath Include="C:\Program Files\Autodesk\eTransmit for Revit 2023\eTransmitForRevitDB.dll" />
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Microsoft.CSharp.dll" />
    <ReferencePath Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\packages\Microsoft.Office.Interop.Excel.15.0.4795.1001\lib\net20\Microsoft.Office.Interop.Excel.dll">
      <EmbedInteropTypes>True</EmbedInteropTypes>
    </ReferencePath>
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll" />
    <ReferencePath Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll" />
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\PresentationCore.dll" />
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\PresentationFramework.dll" />
    <ReferencePath Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\packages\Revit_All_Main_Versions_API_x64.2023.0.0\lib\net48\RevitAPI.dll" />
    <ReferencePath Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\packages\Revit_All_Main_Versions_API_x64.2023.0.0\lib\net48\RevitAPIUI.dll" />
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\System.Core.dll" />
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\System.dll" />
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\System.Windows.Forms.dll" />
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\System.Xaml.dll" />
    <ReferencePath Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\packages\Revit_All_Main_Versions_API_x64.2023.0.0\lib\net48\UIFramework.dll" />
    <ReferencePath Include="C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\WindowsBase.dll" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\MergeDuplicatesWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\NestedDataEditorWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SetTemplateView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\ManageTemplatesView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\ResolveConflictsView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\DropdownSelectionView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SingleItemSelectionWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\ListByCheckboxView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SelectSearchPathsView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\ImportSummaryWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SharedProgressWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\MergeDetailedReviewWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SettingsDashboardWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\WorksetWizardWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\ViewAutoNumWizardWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SyncResolutionWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SyncToastNotification.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SyncWizardWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SyncSettingsView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\StandardsSettingsView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\CategorySelectionControl.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\StandardsClassSelectionControl.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\StandardsReviewWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\ProjectStandardsDashboardWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\SelectRevitDocumentWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\NetworkPathsWizardWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\DetailItemFactoryView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\DetailItemFactoryResultsView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\DetailItemFactorySettingsView.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\GuardrailPromptWindow.g.cs" />
    <Compile Include="C:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2\src\Synthetic2023\obj\Debug\GeneratedInternalTypeHelper.g.cs" />
  </ItemGroup>
</Project>
```

### File: Revit API Synthetic v2/src/Synthetic2024/packages.config
```xml
﻿<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Microsoft.Office.Interop.Excel" version="15.0.4795.1001" targetFramework="net48" />
  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
  <package id="Revit_All_Main_Versions_API_x64" version="2024.2.0" targetFramework="net48" />
</packages>
```

### File: Revit API Synthetic v2/src/Synthetic2024/Synthetic2024.addin
```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Synthetic</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Synthetic2024.dll</Assembly>
    <FullClassName>Synthetic.Core.App</FullClassName>
    <ClientId>4fd5b26e-1325-460f-97b1-878804070868</ClientId>
    <VendorId>net.amcgoey</VendorId>
    <VendorDescription>Synthetic by Arthur McGoey</VendorDescription>
  </AddIn>
</RevitAddIns>
```

### File: Revit API Synthetic v2/src/Synthetic2024/Synthetic2024.csproj
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="14.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch>
      None
    </ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch>
    <TargetFrameworkProfile />
  </PropertyGroup>
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">x64</Platform>
    <ProjectGuid>{9D174A68-34D9-47DD-BF9D-8F12457F16E2}</ProjectGuid>
    <OutputType>Library</OutputType>
    <ProjectTypeGuids>{60dc8134-eba5-43b8-bcc9-bb4bc16c2548};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <AppDesignerFolder>Properties</AppDesignerFolder>
    <RootNamespace>Synthetic</RootNamespace>
    <AssemblyName>Synthetic2024</AssemblyName>
    <RevitVersion>2024</RevitVersion>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <FileAlignment>512</FileAlignment>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <DefineConstants>TRACE;DEBUG;REVIT2024</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <StartAction>Program</StartAction>
    <StartProgram>$(ProgramW6432)\Autodesk\Revit $(RevitVersion)\Revit.exe</StartProgram>
    <DocumentationFile>..\..\output\Synthetic\Synthetic2024.xml</DocumentationFile>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <DefineConstants>TRACE;REVIT2024</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <StartAction>Program</StartAction>
    <StartProgram>$(ProgramW6432)\Autodesk\Revit $(RevitVersion)\Revit.exe</StartProgram>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="AdWindows, Version=5.0.3.1, Culture=neutral, processorArchitecture=MSIL">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2024.2.0\lib\net48\AdWindows.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="eTransmitForRevit">
      <HintPath>C:\Program Files\Autodesk\eTransmit for Revit 2024\eTransmitForRevit.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="eTransmitForRevitDB">
      <HintPath>C:\Program Files\Autodesk\eTransmit for Revit 2024\eTransmitForRevitDB.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="Microsoft.CSharp" />
    <Reference Include="Microsoft.Office.Interop.Excel, Version=15.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c, processorArchitecture=MSIL">
      <HintPath>..\packages\Microsoft.Office.Interop.Excel.15.0.4795.1001\lib\net20\Microsoft.Office.Interop.Excel.dll</HintPath>
      <EmbedInteropTypes>True</EmbedInteropTypes>
    </Reference>
    <Reference Include="Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed, processorArchitecture=MSIL">
      <HintPath>..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll</HintPath>
    </Reference>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="System.Xaml" />
    <Reference Include="WindowsBase" />
    <Reference Include="RevitAPI, Version=24.2.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2024.2.0\lib\net48\RevitAPI.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="RevitAPIUI, Version=24.2.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2024.2.0\lib\net48\RevitAPIUI.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="System" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="UIFramework, Version=24.2.0.0, Culture=neutral, processorArchitecture=AMD64">
      <HintPath>..\packages\Revit_All_Main_Versions_API_x64.2024.2.0\lib\net48\UIFramework.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>
  <ItemGroup>
    <None Include="packages.config" />
    <None Include="Synthetic2024.addin">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
  </ItemGroup>

  <Import Project="..\SyntheticShared\SyntheticShared.projitems" Label="Shared" />
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />

  <!-- Define the Inline C# Task to handle Regex replacement -->
  <UsingTask TaskName="ReplaceAddinPath" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
    <ParameterGroup>
      <InputFile ParameterType="System.String" Required="true" />
      <OutputFile ParameterType="System.String" Required="true" />
      <NewPath ParameterType="System.String" Required="true" />
    </ParameterGroup>
    <Task>
      <Using Namespace="System" />
      <Using Namespace="System.IO" />
      <Using Namespace="System.Text.RegularExpressions" />
      <Code Type="Fragment"><![CDATA[
        try {
            string content = File.ReadAllText(InputFile);
            string pattern = "(?i)<Assembly>.*?</Assembly>";
            string replacement = "<Assembly>" + NewPath.Replace("$", "$$") + "</Assembly>";
            string newContent = Regex.Replace(content, pattern, replacement);
            File.WriteAllText(OutputFile, newContent);
        } catch (Exception ex) {
            Log.LogError("Error replacing addin path: " + ex.Message);
            return false;
        }
      ]]></Code>
    </Task>
  </UsingTask>

  <!-- Deploy and update the .addin file for both Debug and Release configurations -->
  <Target Name="DeployAddin" AfterTargets="Build" Condition="Exists('$(ProjectDir)$(AssemblyName).addin')">
    <PropertyGroup>
      <SourceAddinFile>$(ProjectDir)$(AssemblyName).addin</SourceAddinFile>
      <LocalOutputAddin>$(TargetDir)$(AssemblyName).addin</LocalOutputAddin>
      <!-- Determine correct Assembly Path based on Configuration -->
      <NewAssemblyPath Condition="'$(Configuration)' == 'Debug'">$(TargetPath)</NewAssemblyPath>
      <NewAssemblyPath Condition="'$(Configuration)' != 'Debug'">C:\ProgramData\Autodesk\Revit\Addins\Synthetic\$(AssemblyName).dll</NewAssemblyPath>
    </PropertyGroup>

    <Message Text="Replacing assembly path in $(Configuration) .addin: $(LocalOutputAddin) -> $(NewAssemblyPath)" Importance="high" />
    
    <!-- Execute the C# Inline Task -->
    <ReplaceAddinPath InputFile="$(SourceAddinFile)" OutputFile="$(LocalOutputAddin)" NewPath="$(NewAssemblyPath)" />

    <!-- Copy the Debug manifest directly to AppData Revit Addins folder -->
    <Copy SourceFiles="$(LocalOutputAddin)" DestinationFolder="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\" Condition="'$(Configuration)' == 'Debug'" />
  </Target>

  <Target Name="AfterClean">
    <Delete Files="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\$(AssemblyName).addin" />
  </Target>

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>
</Project>
```

### File: Revit API Synthetic v2/src/Synthetic2025/App.config
```xml
﻿<?xml version="1.0" encoding="utf-8" ?>
<configuration>
</configuration>
```

### File: Revit API Synthetic v2/src/Synthetic2025/Synthetic2025.addin
```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Synthetic</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Synthetic2025.dll</Assembly>
    <FullClassName>Synthetic.Core.App</FullClassName>
    <ClientId>4fd5b26e-1325-460f-97b1-878804070868</ClientId>
    <VendorId>net.amcgoey</VendorId>
    <VendorDescription>Synthetic by Arthur McGoey</VendorDescription>
  </AddIn>
</RevitAddIns>
```

### File: Revit API Synthetic v2/src/Synthetic2025/Synthetic2025.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
    <GenerateDocumentationFile>True</GenerateDocumentationFile>
    <DocumentationFile>..\..\output\Synthetic\Synthetic2025.xml</DocumentationFile>
    <AssemblyName>Synthetic2025</AssemblyName>
    <RevitVersion>2025</RevitVersion>
    <RootNamespace>Synthetic</RootNamespace>
    <UseWindowsForms>true</UseWindowsForms>
    <UseWPF>true</UseWPF>
    <DefineConstants>$(DefineConstants);REVIT2025</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Office.Interop.Excel" Version="15.0.4795.1001" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2025.0.0" />
  </ItemGroup>



  <ItemGroup>
    <Reference Include="eTransmitForRevitDB">
      <HintPath>C:\Program Files\Autodesk\eTransmit for Revit 2025\eTransmitForRevitDB.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <Compile Update="Properties\Resources.Designer.cs">
      <DesignTime>True</DesignTime>
      <AutoGen>True</AutoGen>
      <DependentUpon>Resources.resx</DependentUpon>
    </Compile>
    <Compile Update="Properties\Settings.Designer.cs">
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
      <DependentUpon>Settings.settings</DependentUpon>
    </Compile>
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Update="Properties\Resources.resx">
      <Generator>ResXFileCodeGenerator</Generator>
      <LastGenOutput>Resources.Designer.cs</LastGenOutput>
    </EmbeddedResource>
  </ItemGroup>

  <ItemGroup>
    <None Update="Properties\Settings.settings">
      <Generator>SettingsSingleFileGenerator</Generator>
      <LastGenOutput>Settings.Designer.cs</LastGenOutput>
    </None>
    <None Update="Synthetic2025.addin">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
  </ItemGroup>

  <Import Project="..\SyntheticShared\SyntheticShared.projitems" Label="Shared" />

  <!-- Define the Inline C# Task to handle Regex replacement -->
  <UsingTask TaskName="ReplaceAddinPath" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
    <ParameterGroup>
      <InputFile ParameterType="System.String" Required="true" />
      <OutputFile ParameterType="System.String" Required="true" />
      <NewPath ParameterType="System.String" Required="true" />
    </ParameterGroup>
    <Task>
      <Using Namespace="System" />
      <Using Namespace="System.IO" />
      <Using Namespace="System.Text.RegularExpressions" />
      <Code Type="Fragment"><![CDATA[
        try {
            string content = File.ReadAllText(InputFile);
            string pattern = "(?i)<Assembly>.*?</Assembly>";
            string replacement = "<Assembly>" + NewPath.Replace("$", "$$") + "</Assembly>";
            string newContent = Regex.Replace(content, pattern, replacement);
            File.WriteAllText(OutputFile, newContent);
        } catch (Exception ex) {
            Log.LogError("Error replacing addin path: " + ex.Message);
            return false;
        }
      ]]></Code>
    </Task>
  </UsingTask>

  <!-- Deploy and update the .addin file for both Debug and Release configurations -->
  <Target Name="DeployAddin" AfterTargets="Build" Condition="Exists('$(ProjectDir)$(AssemblyName).addin')">
    <PropertyGroup>
      <SourceAddinFile>$(ProjectDir)$(AssemblyName).addin</SourceAddinFile>
      <LocalOutputAddin>$(TargetDir)$(AssemblyName).addin</LocalOutputAddin>
      <!-- Determine correct Assembly Path based on Configuration -->
      <NewAssemblyPath Condition="'$(Configuration)' == 'Debug'">$(TargetPath)</NewAssemblyPath>
      <NewAssemblyPath Condition="'$(Configuration)' != 'Debug'">C:\ProgramData\Autodesk\Revit\Addins\Synthetic\$(AssemblyName).dll</NewAssemblyPath>
    </PropertyGroup>

    <Message Text="Replacing assembly path in $(Configuration) .addin: $(LocalOutputAddin) -> $(NewAssemblyPath)" Importance="high" />
    
    <!-- Execute the C# Inline Task -->
    <ReplaceAddinPath InputFile="$(SourceAddinFile)" OutputFile="$(LocalOutputAddin)" NewPath="$(NewAssemblyPath)" />

    <!-- Copy the Debug manifest directly to AppData Revit Addins folder -->
    <Copy SourceFiles="$(LocalOutputAddin)" DestinationFolder="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\" Condition="'$(Configuration)' == 'Debug'" />
  </Target>

  <Target Name="AfterClean">
    <Delete Files="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\$(AssemblyName).addin" />
  </Target>

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>
</Project>

```

### File: Revit API Synthetic v2/src/Synthetic2026/App.config
```xml
﻿<?xml version="1.0" encoding="utf-8" ?>
<configuration>
</configuration>
```

### File: Revit API Synthetic v2/src/Synthetic2026/Synthetic2026.addin
```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Synthetic</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Synthetic2026.dll</Assembly>
    <FullClassName>Synthetic.Core.App</FullClassName>
    <ClientId>4fd5b26e-1325-460f-97b1-878804070869</ClientId>
    <VendorId>net.amcgoey</VendorId>
    <VendorDescription>Synthetic by Arthur McGoey</VendorDescription>
  </AddIn>
</RevitAddIns>

```

### File: Revit API Synthetic v2/src/Synthetic2026/Synthetic2026.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <OutputPath>..\..\output\Synthetic\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
    <GenerateDocumentationFile>True</GenerateDocumentationFile>
    <DocumentationFile>..\..\output\Synthetic\Synthetic2026.xml</DocumentationFile>
    <AssemblyName>Synthetic2026</AssemblyName>
    <RevitVersion>2026</RevitVersion>
    <RootNamespace>Synthetic</RootNamespace>
    <UseWindowsForms>true</UseWindowsForms>
    <UseWPF>true</UseWPF>
    <DefineConstants>$(DefineConstants);REVIT2026</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Office.Interop.Excel" Version="15.0.4795.1001" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2026.0.0" />
  </ItemGroup>



  <ItemGroup>
    <Reference Include="eTransmitForRevitDB">
      <HintPath>C:\Program Files\Autodesk\eTransmit for Revit 2026\eTransmitForRevitDB.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <None Update="Synthetic2026.addin">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
  </ItemGroup>

  <Import Project="..\SyntheticShared\SyntheticShared.projitems" Label="Shared" />

  <!-- Define the Inline C# Task to handle Regex replacement -->
  <UsingTask TaskName="ReplaceAddinPath" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
    <ParameterGroup>
      <InputFile ParameterType="System.String" Required="true" />
      <OutputFile ParameterType="System.String" Required="true" />
      <NewPath ParameterType="System.String" Required="true" />
    </ParameterGroup>
    <Task>
      <Using Namespace="System" />
      <Using Namespace="System.IO" />
      <Using Namespace="System.Text.RegularExpressions" />
      <Code Type="Fragment"><![CDATA[
        try {
            string content = File.ReadAllText(InputFile);
            string pattern = "(?i)<Assembly>.*?</Assembly>";
            string replacement = "<Assembly>" + NewPath.Replace("$", "$$") + "</Assembly>";
            string newContent = Regex.Replace(content, pattern, replacement);
            File.WriteAllText(OutputFile, newContent);
        } catch (Exception ex) {
            Log.LogError("Error replacing addin path: " + ex.Message);
            return false;
        }
      ]]></Code>
    </Task>
  </UsingTask>

  <!-- Deploy and update the .addin file for both Debug and Release configurations -->
  <Target Name="DeployAddin" AfterTargets="Build" Condition="Exists('$(ProjectDir)$(AssemblyName).addin')">
    <PropertyGroup>
      <SourceAddinFile>$(ProjectDir)$(AssemblyName).addin</SourceAddinFile>
      <LocalOutputAddin>$(TargetDir)$(AssemblyName).addin</LocalOutputAddin>
      <!-- Determine correct Assembly Path based on Configuration -->
      <NewAssemblyPath Condition="'$(Configuration)' == 'Debug'">$(TargetPath)</NewAssemblyPath>
      <NewAssemblyPath Condition="'$(Configuration)' != 'Debug'">C:\ProgramData\Autodesk\Revit\Addins\Synthetic\$(AssemblyName).dll</NewAssemblyPath>
    </PropertyGroup>

    <Message Text="Replacing assembly path in $(Configuration) .addin: $(LocalOutputAddin) -> $(NewAssemblyPath)" Importance="high" />
    
    <!-- Execute the C# Inline Task -->
    <ReplaceAddinPath InputFile="$(SourceAddinFile)" OutputFile="$(LocalOutputAddin)" NewPath="$(NewAssemblyPath)" />

    <!-- Copy the Debug manifest directly to AppData Revit Addins folder -->
    <Copy SourceFiles="$(LocalOutputAddin)" DestinationFolder="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\" Condition="'$(Configuration)' == 'Debug'" />
  </Target>

  <Target Name="AfterClean">
    <Delete Files="$(AppData)\Autodesk\Revit\Addins\$(RevitVersion)\$(AssemblyName).addin" />
  </Target>

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>
</Project>

```

### File: Revit API Synthetic v2/src/SyntheticShared/SyntheticShared.projitems
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <MSBuildAllProjects Condition="'$(MSBuildVersion)' == '' Or '$(MSBuildVersion)' &lt; '16.0'">$(MSBuildAllProjects);$(MSBuildThisFileFullPath)</MSBuildAllProjects>
    <HasSharedItems>true</HasSharedItems>
    <SharedGUID>c2d5ef8f-47f1-412b-81e0-1a298a5d0019</SharedGUID>
  </PropertyGroup>
  <PropertyGroup Label="Configuration">
    <Import_RootNamespace>Synthetic</Import_RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(MSBuildThisFileDirectory)AssemblyInfo.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Core\App.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\FamilyManagement\Commands\AuditPurgeAllFamilies.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Commands\SettingsDashboardCommand.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\ViewManagement\Commands\ConvertLegendToDrafting.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\ViewManagement\Commands\ConvertDraftingToLegend.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Commands\ElementsOnWorksetReload.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\FamilyManagement\Commands\FamiliesForceReinsert.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MaterialManagement\Commands\MaterialImagesPackage.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MaterialManagement\Commands\MaterialsRepathAll.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MaterialManagement\Commands\PaintElements.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\BatchPrint\Commands\PrintBatchMultiDoc.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Commands\ElementsOnWorksetRecord.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Commands\ScopeBoxesMoveToWorkset.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\Diagnostics\StorageDelete.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\ViewManagement\Commands\ViewAutoNumberConfig.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Commands\WorksetStartView.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\ModelsToSerialize.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\AppearanceAssetModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\BooleanModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\BoundingBoxXYZModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\CategoryGraphicOverrideModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\CategoryIdModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\CategoryModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ColorModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\CompoundStructureModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\DimensionTypeModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ElementIdModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ElementModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ElementTypeModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\EnumModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\FilledRegionTypeModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\FillGridModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\FillPatternModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\FilterRuleModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\HostObjTypeModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ListModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\MaterialModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\MissingModels.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ObjectModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\OverrideGraphicSettingsModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ParameterFilterElementModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ParameterModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ParameterDefinitionSpec.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\PlanViewRangeModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\SerializationResultModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\StructuralAssetModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ThermalAssetModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\TransformModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\UVModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ViewFilterOverrideModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ViewPlanModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ViewScheduleModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\ViewSheetModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Models\XYZModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\AliasSwapEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\GraphicOverrideUtility.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\ImportExecutionRunner.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\IStandardSerializationEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\MaterialAssetEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\ParameterEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\RevitDomDependencyScanner.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\StandardSerializationEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\BoundingBoxTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\BrowserOrganizationTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\CategoryGraphicOverrideTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\CategoryTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\ColorTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\DimensionTypeTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\FilledRegionTypeTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\FillPatternTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\GridTypeTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\HostObjTypeTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\IIdentityService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\IModelTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\IPocoIdentityService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\LevelTypeTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\LinePatternTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\MaterialTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\ModelDispatcher.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\ParameterElementTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\ParameterFilterElementTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\PocoIdentityService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\RevitDomExtensions.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\RevitIdentityService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\SpotDimensionTypeTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\TemplateDuplicatingTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\TextElementTypeTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\TransformTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\ViewPlanTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\ViewScheduleTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\ViewSheetTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\ViewTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Translation\XYZTranslator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Diffing\IDiffEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Diffing\PocoToRevitDiffEngine.cs" />

    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Models\ElementsOnWorkset.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\ConfigCollection.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\ISettingModule.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\Persistence\SyntheticSettingsJsonSchema.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\Persistence\LegacySettingsMigration.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\Persistence\SettingsManager.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\MaterialLibrarySettings.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\ProjectMaterialSettings.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\SyncSettings.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\FileUtilitySettings.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\StandardsSettings.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Core\RibbonManager.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\EnumToBooleanConverter.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\BatchPrint\Utilities\BBPrinterSettingsUtils.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\RevitAPI\CommandUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\RevitAPI\DocumentUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\RevitAPI\ElementUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\EnumUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\RevitAPI\FamilyUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\RevitAPI\FamilySymbolUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\IO\FileUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\ViewManagement\Utilities\LegendsUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MaterialManagement\Utilities\MaterialPathUtils.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MaterialManagement\Utilities\MaterialUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Utilities\ScopeBoxUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\IO\SearchPaths.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\RevitAPI\Select.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsDiffEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\IStandardsExtractionOrchestrator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsExtractionOrchestrator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\IFamilyEnforcer.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\IStandardsExecutionPipeline.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\RevitFamilyEnforcer.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsExecutionPipeline.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\RevitAPI\StorageUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\FamilyManagement\Utilities\SafeFamilyLoadOptions.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\FamilyManagement\Utilities\PurgeFailuresPreprocessor.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\ViewManagement\Models\ViewAutoNumModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\ViewManagement\Utilities\ViewUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\Diagnostics\StorageQuery.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\ViewManagement\Commands\ViewsAutoNumber.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Commands\WorksetSettingsShow.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\Serialization\Json.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\Config.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\ViewAutoNumSettings.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Settings\WorksetSettings.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Utilities\WorksetUtil.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Commands\WorksetSetFile.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Commands\WorksetsImport.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Infrastructure\IO\Excel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\Worksets\Models\WorksetModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\ImportLogItem.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsExecutionItem.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsExecutionOptions.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsExecutionResult.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Merge\DuplicateItemModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Merge\DuplicateClusterModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Merge\DuplicateTypeModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Merge\RecommendedAction.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Merge\TypeMappingModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Merge\ParameterDiffRowModel.cs" />
  </ItemGroup>
  <ItemGroup>
    <Folder Include="$(MSBuildThisFileDirectory)Views\" />
    <Folder Include="$(MSBuildThisFileDirectory)ViewModels\" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\ViewModelBase.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\RelayCommand.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\SharedProgressViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\ParameterWrapperVM.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\ElementTypeWrapperVM.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\ViewModels\MergeQueueViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\ViewModels\MergeDuplicatesViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Services\RevitMergeDataCollector.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\RevitWindowHelper.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Models\TagTemplate.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\RevitAPI\CoordinateUtility.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Merge\MergeAnalysisEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Repositories\TemplateStorageRepository.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\ViewModels\SetTemplateViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Views\SetTemplateView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Commands\CmdSetTemplate.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\ViewModels\ManageTemplatesViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Views\ManageTemplatesView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Commands\CmdManageTemplates.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Commands\CmdBatchTag.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\ViewModels\ResolveConflictsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Views\ResolveConflictsView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\NestedDataEditorViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\NestedDataEditorWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\FileDialogHelper.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\IFileDialogService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\WindowsFileDialogService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\IUserPromptService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\WindowsUserPromptService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\IGuardrailPromptService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\WindowsGuardrailPromptService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\ISummaryDisplayService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\WindowsSummaryDisplayService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\WindowChromeBehavior.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\DropdownSelectionViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\DropdownSelectionView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\ListByCheckboxViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\ListByCheckboxView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\SelectSearchPathsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\SelectSearchPathsView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\ISingleItemSelectionViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\SingleItemSelectionViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\SingleItemSelectionDisplayConverter.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\SingleItemSelectionWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Views\MergeDuplicatesWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Handlers\SyncExternalEventHandler.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\ImportSummaryViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\ImportSummaryWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\SharedProgressWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\ProgressCoordinator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\ProgressState.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\ViewModels\MergeDetailedReviewViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Views\MergeDetailedReviewWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Handlers\ProcessMergeEventHandler.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Commands\CmdMergeDuplicates.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\ISettingModuleViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\WorksetSettingsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\ViewAutoNumSettingsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\MaterialLibraryViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\ProjectMaterialsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\SettingsDashboardViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\WorksetWizardViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\ViewAutoNumWizardViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SettingsDashboardWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\WorksetWizardWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\ViewAutoNumWizardWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\SyncSettingsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\SyncWizardViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SyncResolutionWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SyncToastNotification.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SyncWizardWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SyncSettingsView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\PathMappingViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\FileUtilitySettingsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\NetworkPathsWizardViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\NetworkPathsWizardWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\ViewModels\StandardsSettingsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\StandardsSettingsView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\CategorySelectionViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\CategorySelectionControl.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\StandardsClassSelectionViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\StandardsClassSelectionControl.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\StandardsReviewViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\StandardsReviewWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Commands\CmdProjectStandards.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\SourceTreeItemViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\StandardGroupModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\StandardClassModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\StandardElementModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\ProjectStandardsSourceViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsHierarchyUtility.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\PathResolutionUtility.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsExportService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsMergeUtility.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\IStandardsExportService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\FindReplaceService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\IFindReplaceService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)RevitDOM\Operations\Standards\StandardsReportGenerator.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\ProjectStandardsDashboardViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\StandardsSourceTreeViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\StagingQueueViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\IProjectStandardsDashboard.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\StandardsExecutionPipelineViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\ProjectStandardsDashboardWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\QueueItemModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\ViewModels\SelectRevitDocumentViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\SelectRevitDocumentWindow.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Commands\CmdDetailItemFactory.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\ViewModels\DetailItemFactoryViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\ViewModels\SelectedElementItemViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Views\DetailItemFactoryView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Handlers\DetailItemFactoryEventHandler.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\FamilyManagement\Handlers\AuditPurgeEventHandler.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Models\DetailItemResultItem.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\ViewModels\DetailItemFactoryResultsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Views\DetailItemFactoryResultsView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Settings\DetailItemFactorySettings.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\ViewModels\DetailItemFactorySettingsViewModel.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Views\DetailItemFactorySettingsView.xaml.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\UI\GuardrailPromptWindow.xaml.cs" />
  </ItemGroup>
    <ItemGroup>
    <Page Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Views\MergeDuplicatesWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\NestedDataEditorWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Views\SetTemplateView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Views\ManageTemplatesView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\AutoTagger\Views\ResolveConflictsView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Shared\UI\SyntheticTheme.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Shared\UI\DropdownSelectionView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Shared\UI\SingleItemSelectionWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Shared\UI\ListByCheckboxView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Shared\UI\SelectSearchPathsView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\ImportSummaryWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Shared\UI\SharedProgressWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Views\MergeDetailedReviewWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SettingsDashboardWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\WorksetWizardWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\ViewAutoNumWizardWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>

    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SyncResolutionWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SyncToastNotification.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SyncWizardWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\SyncSettingsView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\StandardsSettingsView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\CategorySelectionControl.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\StandardsClassSelectionControl.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\StandardsReviewWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\ProjectStandardsDashboardWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Views\SelectRevitDocumentWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\SettingsDashboard\Views\NetworkPathsWizardWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Views\DetailItemFactoryView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Views\DetailItemFactoryResultsView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Modules\DetailItemFactory\Views\DetailItemFactorySettingsView.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Shared\UI\GuardrailPromptWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
      <SubType>Designer</SubType>
    </Page>
  </ItemGroup>
  <ItemGroup>
    <Content Include="$(MSBuildThisFileDirectory)Assets\box3d-center.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\autonumber_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\autonumber_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\autotag_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\autotag_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\convert_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\convert_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\family_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\family_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\material_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\material_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\paint_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\paint_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\print_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\print_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\scopebox_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\scopebox_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\settings_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\settings_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\workset_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\workset_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\schema_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\schema_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)SyntheticSettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)SyntheticSettings.template.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\Templates\Detail Item.rft">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\Templates\README.txt">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\ribbon_config.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\placeholder_16.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="$(MSBuildThisFileDirectory)Assets\placeholder_32.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)SyntheticWorksets.xlsx">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>

```

### File: Revit API Synthetic v2/src/SyntheticShared/SyntheticShared.shproj
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup Label="Globals">
    <ProjectGuid>c2d5ef8f-47f1-412b-81e0-1a298a5d0019</ProjectGuid>
    <MinimumVisualStudioVersion>14.0</MinimumVisualStudioVersion>
  </PropertyGroup>
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <Import Project="$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\CodeSharing\Microsoft.CodeSharing.Common.Default.props" />
  <Import Project="$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\CodeSharing\Microsoft.CodeSharing.Common.props" />
  <PropertyGroup />
  <Import Project="SyntheticShared.projitems" Label="Shared" />
  <Import Project="$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\CodeSharing\Microsoft.CodeSharing.CSharp.targets" />
</Project>
```

### File: Revit API Synthetic v2/tests/RevitAPIMock/RevitAPIMock.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows8.0</TargetFramework>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>RevitAPI</AssemblyName>
    <RootNamespace>Autodesk.Revit</RootNamespace>
  </PropertyGroup>

</Project>
```

### File: Revit API Synthetic v2/tests/RevitAPIUIMock/RevitAPIUIMock.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows8.0</TargetFramework>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>RevitAPIUI</AssemblyName>
    <RootNamespace>Autodesk.Revit.UI</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\RevitAPIMock\RevitAPIMock.csproj" />
  </ItemGroup>

</Project>
```

### File: Revit API Synthetic v2/tests/SyntheticTests.Logic/SyntheticTests.Logic.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>SyntheticTests.Logic</AssemblyName>
    <RootNamespace>SyntheticTests</RootNamespace>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Synthetic2026\Synthetic2026.csproj" />
    <Compile Include="..\SyntheticTests.Shared\Modules\RevitDOM\FakeIdentityService.cs" Link="Modules\RevitDOM\FakeIdentityService.cs" />
    <Compile Include="..\SyntheticTests.Shared\Modules\StandardsManagement\StandardsExecutionPipelineTests.cs" Link="Modules\StandardsManagement\StandardsExecutionPipelineTests.cs" />
    <Compile Include="..\SyntheticTests.Shared\Modules\StandardsManagement\DashboardTestFactory.cs" Link="Modules\StandardsManagement\DashboardTestFactory.cs" />
    <Compile Include="..\SyntheticTests.Shared\Modules\StandardsManagement\FakeFileDialogService.cs" Link="Modules\StandardsManagement\FakeFileDialogService.cs" />
    <Compile Include="..\SyntheticTests.Shared\Modules\StandardsManagement\FakeGuardrailPromptService.cs" Link="Modules\StandardsManagement\FakeGuardrailPromptService.cs" />
    <Compile Include="..\SyntheticTests.Shared\Modules\StandardsManagement\FakeUserPromptService.cs" Link="Modules\StandardsManagement\FakeUserPromptService.cs" />
    <Compile Include="..\SyntheticTests.Shared\Modules\MergeDuplicates\Tier2_MergeDuplicatesHeadlessTests.cs" Link="Modules\MergeDuplicates\Tier2_MergeDuplicatesHeadlessTests.cs" />
    <Compile Remove="Infrastructure\UI\RibbonTests.cs" />
    <Compile Include="Infrastructure\UI\RibbonTests.cs" />
  </ItemGroup>

  <!-- Compile mock RevitAPI and RevitAPIUI projects and copy them to output directory post-build -->
    <Target Name="CopyMockRevitAPI" AfterTargets="Build">
    <MSBuild Projects="..\RevitAPIMock\RevitAPIMock.csproj" Targets="Build" Properties="Configuration=$(Configuration);Platform=$(Platform)" />
    <Copy SourceFiles="$(LocalAppData)\Temp\bin\RevitAPIMock\net8.0-windows8.0\RevitAPI.dll" DestinationFolder="$(TargetDir)" OverwriteReadOnlyFiles="true" SkipUnchangedFiles="false" />

    <MSBuild Projects="..\RevitAPIUIMock\RevitAPIUIMock.csproj" Targets="Build" Properties="Configuration=$(Configuration);Platform=$(Platform)" />
    <Copy SourceFiles="$(LocalAppData)\Temp\bin\RevitAPIUIMock\net8.0-windows8.0\RevitAPIUI.dll" DestinationFolder="$(TargetDir)" OverwriteReadOnlyFiles="true" SkipUnchangedFiles="false" />
  </Target>

</Project>

```

### File: Revit API Synthetic v2/tests/SyntheticTests.Shared/SyntheticTests.Shared.projitems
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <MSBuildAllProjects Condition="'$(MSBuildVersion)' == '' Or '$(MSBuildVersion)' &lt; '16.0'">$(MSBuildAllProjects);$(MSBuildThisFileFullPath)</MSBuildAllProjects>
    <HasSharedItems>true</HasSharedItems>
    <SharedGUID>{8d22ef1f-d224-4f91-baae-5a2a688b14e3}</SharedGUID>
  </PropertyGroup>
  <PropertyGroup Label="Configuration">
    <Import_RootNamespace>SyntheticTests</Import_RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(MSBuildThisFileDirectory)Tier2_RevitSmokeTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\CategoryModelTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\FakeIdentityService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\ParameterEngineTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\RevitIdentityServiceIntegrationTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\Tier2_StandardsExtractionOrchestratorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\RevitIdentityServiceTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\RevitDomExtensionsTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\StandardSerializationEngineTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\FakeStandardSerializationEngine.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\Tier2_DispatcherSweepTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\LinePatternTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\FillPatternTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\MaterialAssetEngineTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\MaterialTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\DatumAndAnnotationTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\DimensionTypeTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\CategoryTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\ColorTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\AliasSwapEngineTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\Tier2_AliasAssimilationTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\ParameterModelTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\ParameterDefinitionSpecTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\ViewModelTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\ParameterFilterElementTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\FilledRegionTypeTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\HostObjTypeTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\SpatialModelTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\ViewTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\ParameterElementTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\RevitDOM\BrowserOrganizationTranslatorTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Tier2_DashboardIntegrationTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\Tier2_StandardsDiffEngineTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\StandardsExecutionPipelineTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\DashboardTestFactory.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\FakeFileDialogService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\FakeGuardrailPromptService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\StandardsManagement\FakeUserPromptService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\AssemblyAnalyzer\Tier2_AssemblyAnalyzerTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Tier2_MergeDuplicatesHeadlessTests.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)Modules\MergeDuplicates\Tier2_MergeDuplicatesIntegrationTests.cs" />
  </ItemGroup>
</Project>

```

### File: Revit API Synthetic v2/tests/SyntheticTests.Shared/SyntheticTests.Shared.shproj
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup Label="Globals">
    <ProjectGuid>{8d22ef1f-d224-4f91-baae-5a2a688b14e3}</ProjectGuid>
    <MinimumVisualStudioVersion>14.0</MinimumVisualStudioVersion>
  </PropertyGroup>
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <Import Project="$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\CodeSharing\Microsoft.CodeSharing.Common.Default.props" />
  <Import Project="$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\CodeSharing\Microsoft.CodeSharing.Common.props" />
  <PropertyGroup />
  <Import Project="SyntheticTests.Shared.projitems" Label="Shared" />
  <Import Project="$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\CodeSharing\Microsoft.CodeSharing.CSharp.targets" />
</Project>
```

### File: Revit API Synthetic v2/tests/SyntheticTests2023/SyntheticTests2023.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>SyntheticTests2023</AssemblyName>
    <RootNamespace>SyntheticTests</RootNamespace>
    <IsTestProject>true</IsTestProject>
    <DefineConstants>$(DefineConstants);REVIT2023</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NUnit" Version="3.13.3" />
    <PackageReference Include="ricaun.RevitTest.TestAdapter" Version="1.11.1" />
    <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2023.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Synthetic2023\Synthetic2023.csproj" Properties="Platform=AnyCPU" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyMetadata Include="NUnit.Version" Value="2023" />
    <AssemblyMetadata Include="ricaun.RevitTest.Timeout" Value="600000" />
  </ItemGroup>

  <Import Project="..\SyntheticTests.Shared\SyntheticTests.Shared.projitems" Label="Shared" />

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>

  <ItemGroup>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="WindowsBase" />
    <Reference Include="System.Xaml" />
  </ItemGroup>
</Project>
```

### File: Revit API Synthetic v2/tests/SyntheticTests2024/SyntheticTests2024.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>SyntheticTests2024</AssemblyName>
    <RootNamespace>SyntheticTests</RootNamespace>
    <IsTestProject>true</IsTestProject>
    <DefineConstants>$(DefineConstants);REVIT2024</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NUnit" Version="3.13.3" />
    <PackageReference Include="ricaun.RevitTest.TestAdapter" Version="1.11.1" />
    <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2024.2.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Synthetic2024\Synthetic2024.csproj" Properties="Platform=AnyCPU" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyMetadata Include="NUnit.Version" Value="2024" />
    <AssemblyMetadata Include="ricaun.RevitTest.Timeout" Value="600000" />
  </ItemGroup>

  <Import Project="..\SyntheticTests.Shared\SyntheticTests.Shared.projitems" Label="Shared" />

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>

  <ItemGroup>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="WindowsBase" />
    <Reference Include="System.Xaml" />
  </ItemGroup>
</Project>
```

### File: Revit API Synthetic v2/tests/SyntheticTests2025/SyntheticTests2025.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>SyntheticTests2025</AssemblyName>
    <RootNamespace>SyntheticTests</RootNamespace>
    <IsTestProject>true</IsTestProject>
    <DefineConstants>$(DefineConstants);REVIT2025</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NUnit" Version="3.13.3" />
    <PackageReference Include="ricaun.RevitTest.TestAdapter" Version="1.11.1" />
    <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2025.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Synthetic2025\Synthetic2025.csproj" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyMetadata Include="NUnit.Version" Value="2025" />
    <AssemblyMetadata Include="ricaun.RevitTest.Timeout" Value="600000" />
  </ItemGroup>

  <Import Project="..\SyntheticTests.Shared\SyntheticTests.Shared.projitems" Label="Shared" />

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>

  <ItemGroup>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="WindowsBase" />
    <Reference Include="System.Xaml" />
  </ItemGroup>
</Project>
```

### File: Revit API Synthetic v2/tests/SyntheticTests2026/SyntheticTests2026.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows8.0</TargetFramework>
    <BaseIntermediateOutputPath>$(LocalAppData)\Temp\obj\SyntheticTests2026_ff610c80\</BaseIntermediateOutputPath>
    <OutputPath>$(LocalAppData)\Temp\bin\SyntheticTests2026_ff610c80\</OutputPath>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>SyntheticTests2026</AssemblyName>
    <RootNamespace>SyntheticTests</RootNamespace>
    <IsTestProject>true</IsTestProject>
    <DefineConstants>$(DefineConstants);REVIT2026</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NUnit" Version="3.13.3" />
    <PackageReference Include="ricaun.RevitTest.TestAdapter" Version="1.11.1" />
    <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2026.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Synthetic2026\Synthetic2026.csproj" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyMetadata Include="NUnit.Version" Value="2026" />
    <AssemblyMetadata Include="ricaun.RevitTest.Timeout" Value="600000" />
  </ItemGroup>

  <Import Project="..\SyntheticTests.Shared\SyntheticTests.Shared.projitems" Label="Shared" />

  <Target Name="SignDebugBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <PropertyGroup>
      <WindowsKitsRoot>$(registry:HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots@KitsRoot10)</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', '', 'Registry64', 'Registry32'))</WindowsKitsRoot>
      <WindowsKitsRoot Condition="'$(WindowsKitsRoot)' == ''">C:\Program Files (x86)\Windows Kits\10\</WindowsKitsRoot>
      
      <!-- Trim trailing backslash to prevent double-backslash issues in wildcard matching -->
      <WindowsKitsRootTrimmed>$(WindowsKitsRoot.TrimEnd('\'))</WindowsKitsRootTrimmed>
    </PropertyGroup>
    
    <ItemGroup>
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\10.*\x64\signtool.exe" />
      <SignToolPaths Include="$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe" Condition="Exists('$(WindowsKitsRootTrimmed)\bin\x64\signtool.exe')" />
    </ItemGroup>
    
    <PropertyGroup>
      <SignToolExe>%(SignToolPaths.Identity)</SignToolExe>
    </PropertyGroup>

    <Message Importance="High" Text="Dynamically discovered signtool at: $(SignToolExe)" />
    <Message Importance="High" Text="Signing local Debug build with self-signed certificate..." />
    
    <Exec Command="&quot;$(SignToolExe)&quot; sign /n &quot;Synthetic&quot; /fd SHA256 /a &quot;$(TargetPath)&quot;" IgnoreExitCode="true" />
  </Target>
</Project>




```

