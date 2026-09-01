namespace ToDoTree.Core.Tests;

/// <summary>
/// 外部パッケージ無しで動かすための最小テストハーネス。
/// （NuGet が使える環境なら xUnit に置き換えても構いません）
/// </summary>
public static class MiniTest
{
    private static readonly List<(string Name, Action Body)> Cases = [];

    public static void Case(string name, Action body) => Cases.Add((name, body));

    public static int Run()
    {
        var failed = 0;
        foreach (var (name, body) in Cases)
        {
            try
            {
                body();
                Console.WriteLine($"  OK   {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAIL {name}");
                Console.WriteLine($"       {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{Cases.Count - failed} passed / {Cases.Count} total");
        return failed == 0 ? 0 : 1;
    }

    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"期待: {message}");
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"{message} / expected={expected} actual={actual}");
        }
    }
}
