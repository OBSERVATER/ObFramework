namespace ObFramework.Cli.Unity;

internal static partial class UnityTestScaffold
{
    private const string AssemblyDefinition = """
        {
          "name": "Game.Core",
          "rootNamespace": "Game",
          "noEngineReferences": true
        }
        """;

    private const string CoreProjectText = """
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="../ObFramework.Unity.props" />
          <PropertyGroup>
            <TargetFramework>netstandard2.1</TargetFramework>
            <LangVersion>9.0</LangVersion>
            <AssemblyName>$(ObFrameworkBusinessAssembly)</AssemblyName>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
            <BusinessDirectory>$([MSBuild]::NormalizePath('$(MSBuildThisFileDirectory)', '../../$(ObFrameworkBusinessSource)'))</BusinessDirectory>
          </PropertyGroup>
          <ItemGroup>
            <ExcludedAssembly Include="$(BusinessDirectory)/*/**/*.asmdef;$(BusinessDirectory)/*/**/*.asmref" />
            <Compile Include="$(BusinessDirectory)/**/*.cs"
                     Exclude="@(ExcludedAssembly->'%(RootDir)%(Directory)**/*.cs');$(BusinessDirectory)/**/*~/**/*.cs"
                     Link="%(RecursiveDir)%(Filename)%(Extension)" />
            <PackageReference Include="ObFramework" Version="$(ObFrameworkPackageVersion)" />
            <Analyzer Include="../../Assets/ObFramework/Analyzers/ObFramework.Generator.dll" />
          </ItemGroup>
        </Project>
        """;

    private const string TestProjectText = """
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="../ObFramework.Unity.props" />
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <IsTestProject>true</IsTestProject>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.11.0" />
            <PackageReference Include="NUnit" Version="5.0.0" />
            <PackageReference Include="NUnit3TestAdapter" Version="6.3.1" PrivateAssets="all" />
            <ProjectReference Include="../Game.Core/Game.Core.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string Application = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using ObFramework.Core;

        namespace Game
        {
            public sealed class GameApplication : PluginApplication
            {
                protected override IPlugin[] CreatePlugins() => Array.Empty<IPlugin>();

                protected override IArchitecture CreateArchitecture(IPluginContext plugins)
                {
                    GameArchitecture.InitInterface(plugins);
                    return GameArchitecture.Interface;
                }

                protected override Task EnterGameAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class GameArchitecture : AArchitecture<GameArchitecture>
            {
                protected override void Init() { }
            }
        }
        """;

    private const string StartupTest = """
        using System.Threading.Tasks;
        using Game;
        using NUnit.Framework;

        namespace Game.Tests;

        public class GameApplicationTests
        {
            [Test]
            public async Task StartsAndStopsBusinessArchitecture()
            {
                var application = new GameApplication();
                await application.StartAsync();
                var architecture = (GameArchitecture)application.Architecture;
                try
                {
                    Assert.That(architecture.Initialized, Is.True);
                }
                finally
                {
                    await application.StopAsync();
                }

                Assert.That(architecture.Initialized, Is.False);
            }
        }
        """;
}
