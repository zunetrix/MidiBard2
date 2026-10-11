using System;
using System.Numerics;
using MidiBard;

namespace MidiBard.Tests.UI;

public class ThemeTests
{
    [Fact]
    public void ExportAndImport_RoundTrip_PreservesThemeData()
    {
        // Arrange
        var original = new CustomThemeData
        {
            Name = "Vibrant Synthwave",
            WindowBg = new Vector4(0.1f, 0.2f, 0.3f, 0.9f),
            Text = new Vector4(1f, 1f, 1f, 1f),
            Button = new Vector4(0.8f, 0.2f, 0.5f, 1f),
            WindowRounding = 8f,
            FrameRounding = 4f,
            ScrollbarRounding = null,
        };

        // Act
        string exported = original.ExportToString();
        exported.ShouldNotBeNullOrWhiteSpace();

        var imported = CustomThemeData.ImportFromString(exported);

        // Assert
        imported.ShouldNotBeNull();
        imported.Id.ShouldNotBe(original.Id); // fresh GUID assigned
        imported.Name.ShouldBe("Vibrant Synthwave");
        imported.WindowBg.ShouldBe(original.WindowBg);
        imported.Text.ShouldBe(original.Text);
        imported.Button.ShouldBe(original.Button);
        imported.WindowRounding.ShouldBe(8f);
        imported.FrameRounding.ShouldBe(4f);
        imported.ScrollbarRounding.ShouldBeNull();
    }

    [Fact]
    public void ImportFromString_RawJson_ImportsCorrectly()
    {
        var rawJson = "{\"Name\":\"Raw Theme\",\"WindowBg\":{\"X\":0.5,\"Y\":0.5,\"Z\":0.5,\"W\":1.0},\"WindowRounding\":5.0}";

        var imported = CustomThemeData.ImportFromString(rawJson);

        imported.ShouldNotBeNull();
        imported.Name.ShouldBe("Raw Theme");
        imported.WindowBg.ShouldBe(new Vector4(0.5f, 0.5f, 0.5f, 1f));
        imported.WindowRounding.ShouldBe(5f);
    }

    [Fact]
    public void ImportFromString_MarkdownCodeFences_StripsAndImports()
    {
        var original = new CustomThemeData
        {
            Name = "Fenced Theme",
            WindowBg = new Vector4(0.2f, 0.3f, 0.4f, 1.0f),
        };
        string base64 = original.ExportToString();
        string fenced = $"```\n{base64}\n```";

        var imported = CustomThemeData.ImportFromString(fenced);

        imported.ShouldNotBeNull();
        imported.Name.ShouldBe("Fenced Theme");
        imported.WindowBg.ShouldBe(original.WindowBg);
    }

    [Fact]
    public void ImportFromString_EmptyOrWhitespace_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => CustomThemeData.ImportFromString(""));
        Should.Throw<ArgumentException>(() => CustomThemeData.ImportFromString("   "));
    }

    [Fact]
    public void ImportFromString_InvalidData_ThrowsException()
    {
        Should.Throw<Exception>(() => CustomThemeData.ImportFromString("not-valid-base64-or-json!@#$%"));
    }
}
