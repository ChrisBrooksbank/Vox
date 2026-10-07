using Vox.Core.Input;

namespace Vox.Core.Text;

/// <summary>
/// The keys that move the caret in an edit control and the unit to read after each:
/// Left/Right a character, Up/Down a line, Ctrl+Left/Right a word, Ctrl+Up/Down a paragraph,
/// Home/End, Ctrl+Home/End and Page Up/Down the line. With Shift they extend the selection.
/// </summary>
public static class CaretKeys
{
    public const int VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24;
    public const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

    public static bool TryGetUnit(int vkCode, KeyModifiers modifiers, out TextUnit unit, out bool extendsSelection)
    {
        extendsSelection = (modifiers & KeyModifiers.Shift) != 0;
        bool ctrl = (modifiers & KeyModifiers.Ctrl) != 0;
        unit = TextUnit.Character;

        // Alt and the screen reader key make these keys something else entirely
        if ((modifiers & (KeyModifiers.Alt | KeyModifiers.Insert)) != 0)
            return false;

        switch (vkCode)
        {
            case VK_LEFT or VK_RIGHT:
                unit = ctrl ? TextUnit.Word : TextUnit.Character;
                return true;
            case VK_UP or VK_DOWN:
                unit = ctrl ? TextUnit.Paragraph : TextUnit.Line;
                return true;
            case VK_HOME or VK_END or VK_PRIOR or VK_NEXT:
                unit = TextUnit.Line;
                return true;
            default:
                return false;
        }
    }
}
