namespace Vox.Core.Input;

public interface IKeyboardHook
{
    event EventHandler<KeyEvent> KeyPressed;
    void Install();
    void Uninstall();
}
