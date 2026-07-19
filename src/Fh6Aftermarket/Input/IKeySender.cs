namespace Fh6Aftermarket.Input;

public interface IKeySender
{
    void Send(string key);

    void Hold(string key, int milliseconds);

    void KeyDown(string key);

    void KeyUp(string key);

    bool IsDown(string key);
}
