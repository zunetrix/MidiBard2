using Dalamud.Bindings.ImGui;

using MidiBard.Control;
using MidiBard.Util;
using MidiBard.Util.ImGuiExt;

namespace MidiBard;

public sealed class ImGuiComponentsDebug : Widget
{
    public override string Title => "ImGui Components";
    private readonly ImGuiInputAutocompleteInstrument<Instrument> _instrumentSearch = new();
    private string instrumentInput = "";

    public ImGuiComponentsDebug(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        DrawInputAutocomplete();
    }

    private void DrawInputAutocomplete()
    {
        ImGui.SetNextItemWidth(300);
        _instrumentSearch.Draw(
            "Instrument",
            ref instrumentInput,
            InstrumentHelper.Instruments,
            x => x.InstrumentString,
            x => x.IconId);
    }


}

