using Wmux.Core;

namespace Wmux.Tests;

[TestFixture]
public class ScrollbackBufferTests
{
    // ── Constructor ──────────────────────────────────────────────────

    [Test]
    public void Constructor_DefaultCapacity()
    {
        var buf = new ScrollbackBuffer();
        Assert.That(buf.Capacity, Is.EqualTo(10000));
        Assert.That(buf.Count, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_CustomCapacity()
    {
        var buf = new ScrollbackBuffer(500);
        Assert.That(buf.Capacity, Is.EqualTo(500));
    }

    // ── Add / GetLine ────────────────────────────────────────────────

    [Test]
    public void Add_IncreasesCount()
    {
        var buf = new ScrollbackBuffer(100);
        buf.Add("line 1");
        Assert.That(buf.Count, Is.EqualTo(1));
    }

    [Test]
    public void Add_MultipleLines()
    {
        var buf = new ScrollbackBuffer(100);
        buf.Add("first");
        buf.Add("second");
        buf.Add("third");
        Assert.That(buf.Count, Is.EqualTo(3));
    }

    [Test]
    public void GetLine_ReturnsCorrectLine()
    {
        var buf = new ScrollbackBuffer(100);
        buf.Add("alpha");
        buf.Add("beta");
        buf.Add("gamma");

        Assert.That(new string(buf.GetLine(0)!.Value.Chars), Is.EqualTo("alpha"));
        Assert.That(new string(buf.GetLine(1)!.Value.Chars), Is.EqualTo("beta"));
        Assert.That(new string(buf.GetLine(2)!.Value.Chars), Is.EqualTo("gamma"));
    }

    [Test]
    public void GetLine_InvalidIndex_ReturnsNull()
    {
        var buf = new ScrollbackBuffer(100);
        buf.Add("line");

        Assert.That(buf.GetLine(-1), Is.Null);
        Assert.That(buf.GetLine(1), Is.Null);
        Assert.That(buf.GetLine(100), Is.Null);
    }

    [Test]
    public void GetLine_EmptyBuffer_ReturnsNull()
    {
        var buf = new ScrollbackBuffer(100);
        Assert.That(buf.GetLine(0), Is.Null);
    }

    // ── Ring buffer wrapping ─────────────────────────────────────────

    [Test]
    public void RingBuffer_OverwritesOldest()
    {
        var buf = new ScrollbackBuffer(3);
        buf.Add("A");
        buf.Add("B");
        buf.Add("C");
        buf.Add("D"); // Should overwrite "A"

        Assert.That(buf.Count, Is.EqualTo(3));
        Assert.That(new string(buf.GetLine(0)!.Value.Chars), Is.EqualTo("B"));
        Assert.That(new string(buf.GetLine(1)!.Value.Chars), Is.EqualTo("C"));
        Assert.That(new string(buf.GetLine(2)!.Value.Chars), Is.EqualTo("D"));
    }

    [Test]
    public void RingBuffer_MultipleWraps()
    {
        var buf = new ScrollbackBuffer(3);
        for (int i = 0; i < 10; i++)
            buf.Add($"line-{i}");

        Assert.That(buf.Count, Is.EqualTo(3));
        Assert.That(new string(buf.GetLine(0)!.Value.Chars), Is.EqualTo("line-7"));
        Assert.That(new string(buf.GetLine(1)!.Value.Chars), Is.EqualTo("line-8"));
        Assert.That(new string(buf.GetLine(2)!.Value.Chars), Is.EqualTo("line-9"));
    }

    [Test]
    public void RingBuffer_CountNeverExceedsCapacity()
    {
        var buf = new ScrollbackBuffer(5);
        for (int i = 0; i < 100; i++)
        {
            buf.Add($"line-{i}");
            Assert.That(buf.Count, Is.LessThanOrEqualTo(5));
        }
    }

    // ── Clear ────────────────────────────────────────────────────────

    [Test]
    public void Clear_ResetsCount()
    {
        var buf = new ScrollbackBuffer(100);
        buf.Add("A");
        buf.Add("B");
        buf.Clear();

        Assert.That(buf.Count, Is.EqualTo(0));
    }

    [Test]
    public void Clear_AfterClear_GetLineReturnsNull()
    {
        var buf = new ScrollbackBuffer(100);
        buf.Add("A");
        buf.Clear();

        Assert.That(buf.GetLine(0), Is.Null);
    }

    [Test]
    public void Clear_CanAddAfterClear()
    {
        var buf = new ScrollbackBuffer(100);
        buf.Add("old");
        buf.Clear();
        buf.Add("new");

        Assert.That(buf.Count, Is.EqualTo(1));
        Assert.That(new string(buf.GetLine(0)!.Value.Chars), Is.EqualTo("new"));
    }

    // ── Edge cases ───────────────────────────────────────────────────

    [Test]
    public void Capacity1_OnlyHoldsOneLine()
    {
        var buf = new ScrollbackBuffer(1);
        buf.Add("first");
        buf.Add("second");

        Assert.That(buf.Count, Is.EqualTo(1));
        Assert.That(new string(buf.GetLine(0)!.Value.Chars), Is.EqualTo("second"));
    }

    [Test]
    public void EmptyStrings_Stored()
    {
        var buf = new ScrollbackBuffer(10);
        buf.Add("");
        Assert.That(buf.Count, Is.EqualTo(1));
        Assert.That(new string(buf.GetLine(0)!.Value.Chars), Is.EqualTo(""));
    }

    // ── Rich line data ──────────────────────────────────────────────

    [Test]
    public void Add_RichLine_PreservesColors()
    {
        var buf = new ScrollbackBuffer(10);
        var line = new ScrollbackLine(
            new[] { 'H', 'i' },
            new[] { ConsoleColor.Red, ConsoleColor.Green },
            new[] { ConsoleColor.Black, ConsoleColor.Blue });
        buf.Add(line);

        var got = buf.GetLine(0)!.Value;
        Assert.That(got.Chars, Is.EqualTo(new[] { 'H', 'i' }));
        Assert.That(got.Fg, Is.EqualTo(new[] { ConsoleColor.Red, ConsoleColor.Green }));
        Assert.That(got.Bg, Is.EqualTo(new[] { ConsoleColor.Black, ConsoleColor.Blue }));
    }
}
