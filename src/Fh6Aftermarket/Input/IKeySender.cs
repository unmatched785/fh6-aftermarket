namespace Fh6Aftermarket.Input;

public interface IKeySender
{
    void Send(string key);

    bool IsDown(string key);
}
