namespace Griddo.Editing;

public interface IGriddoCellEditor
{
    bool CanStartWith(char inputChar);
    string BeginEdit(object? currentValue, char? firstCharacter = null);
    bool TryCommit(string editBuffer, out object? newValue);
}

public interface IGriddoOptionsCellEditor : IGriddoCellEditor
{
    IReadOnlyList<string> Options { get; }
    bool AllowMultiple { get; }
    IReadOnlyList<string> ParseValues(string editBuffer);
    string FormatValues(IEnumerable<string> values);
}

public interface IGriddoContextualOptionsCellEditor : IGriddoOptionsCellEditor
{
    IReadOnlyList<string> GetOptions(object? recordSource);
    bool TryGetOptionExample(object? recordSource, string option, out string example);
}

public interface IGriddoSwatchOptionsCellEditor : IGriddoOptionsCellEditor
{
    bool TryGetSwatchBrush(string option, out System.Windows.Media.Brush brush);
}

public interface IGriddoDialogButtonCellEditor : IGriddoCellEditor
{
    string ButtonText { get; }
    string LaunchToken { get; }
}
