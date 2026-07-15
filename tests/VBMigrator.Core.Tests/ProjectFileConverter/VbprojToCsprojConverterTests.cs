using VBMigrator.Core.ProjectFileConverter;
using Xunit;

namespace VBMigrator.Core.Tests.ProjectFileConverter;

public class VbprojToCsprojConverterTests
{
    private static readonly string OldStyleVbproj = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <RootNamespace>MyApp</RootNamespace>
            <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="Form1.vb" />
            <Import Project="$(MSBuildBinPath)\Microsoft.VisualBasic.targets" />
            <Reference Include="Microsoft.VisualBasic" />
          </ItemGroup>
        </Project>
        """;

    private static readonly string SdkStyleVbproj = """
        <Project Sdk="Microsoft.VisualBasic.App">
          <PropertyGroup>
            <TargetFramework>net48</TargetFramework>
            <RootNamespace>MyApp</RootNamespace>
          </PropertyGroup>
        </Project>
        """;

    [Fact]
    public void Convert_OldStyle_ReplacesCompileAndRemovesVbRefs()
    {
        var result = VbprojToCsprojConverter.Convert(OldStyleVbproj);

        Assert.Contains("Microsoft.NET.Sdk", result);
        Assert.Contains("Form1.cs", result);
        Assert.DoesNotContain("Form1.vb", result);
        Assert.DoesNotContain("Microsoft.VisualBasic.targets", result);
        Assert.DoesNotContain("Microsoft.VisualBasic\"", result);
        Assert.Contains("MyApp", result); // RootNamespace preserved
    }

    [Fact]
    public void Convert_SdkStyle_UpdatesSdkAttribute()
    {
        var result = VbprojToCsprojConverter.Convert(SdkStyleVbproj);

        Assert.Contains("Microsoft.NET.Sdk\"", result);
        Assert.DoesNotContain("Microsoft.VisualBasic.App", result);
        Assert.Contains("MyApp", result);
    }

    [Fact]
    public void Convert_ComReference_PreservesWithComment()
    {
        const string vbproj = """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <COMReference Include="Excel.Application" />
              </ItemGroup>
            </Project>
            """;

        var result = VbprojToCsprojConverter.Convert(vbproj);

        Assert.Contains("COMReference", result);
        Assert.Contains("VBMigrator: COM reference", result);
    }
}
