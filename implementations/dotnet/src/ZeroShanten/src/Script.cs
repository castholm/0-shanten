namespace ZeroShanten;

public class Script
{
    readonly List<byte> _instructions;

    public Script(ReadOnlySpan<char> code)
    {
        _instructions = new List<byte>(13);

        var currentLine = 1;
        var currentColumn = 1;
        var cr = false;

        Span<int> currentHand = stackalloc int[34];
        var currentHandTileCount = 0;
        var currentHandStartLine = 0;
        var currentHandStartColumn = 0;

        var runeEnumerator = code.EnumerateRunes();
        while (runeEnumerator.MoveNext())
        {
            var codePoint = runeEnumerator.Current.Value;

            switch (codePoint)
            {
            case CodePoints.LF:
                if (!cr)
                {
                    currentLine++;
                    currentColumn = 1;
                }
                cr = false;
                break;

            case CodePoints.CR:
                currentLine++;
                currentColumn = 1;
                cr = true;
                break;

            case >= CodePoints.MahjongTilesStart and <= CodePoints.MahjongTilesEnd:
                var tile = codePoint switch
                {
                    >= CodePoints.Man1 and <= CodePoints.Man9 => codePoint - CodePoints.Man1 + Tiles.Man1,
                    >= CodePoints.Pin1 and <= CodePoints.Pin9 => codePoint - CodePoints.Pin1 + Tiles.Pin1,
                    >= CodePoints.Sou1 and <= CodePoints.Sou9 => codePoint - CodePoints.Sou1 + Tiles.Sou1,
                    >= CodePoints.East and <= CodePoints.North => codePoint - CodePoints.East + Tiles.East,
                    CodePoints.White => Tiles.White,
                    CodePoints.Green => Tiles.Green,
                    CodePoints.Red => Tiles.Red,
                    _ => throw new ScriptException($"{currentLine}:{currentColumn}: Error: Invalid character."),
                };

                _instructions.Add((byte)tile);

                if (currentHand[tile] == 4)
                {
                    throw new ScriptException($"{currentLine}:{currentColumn}: Error: Fifth copy of tile.");
                }

                currentHand[tile]++;
                if (currentHandTileCount == 0)
                {
                    currentHandStartLine = currentLine;
                    currentHandStartColumn = currentColumn;
                }
                currentHandTileCount++;

                if (currentHandTileCount == 13)
                {
                    var shanten = Shanten.CalculateShanten(currentHand);
                    if (shanten != 0)
                    {
                        throw new ScriptException($"{currentHandStartLine}:{currentHandStartColumn}: Error: Hand is not in a tenpai state (actual shanten: {shanten}).");
                    }

                    currentHand.Clear();
                    currentHandTileCount = 0;
                }

                goto default;
            default:
                currentColumn++;
                cr = false;
                break;
            }
        }

        if (currentHandTileCount != 0)
        {
            throw new ScriptException($"{currentHandStartLine}:{currentHandStartColumn}: Error: Hand contains too few tiles.");
        }

        _instructions.TrimExcess();
    }

    public int Invoke(Stream? input = null, Stream? output = null)
    {
        Span<int> stack = stackalloc int[13];
        var stackCount = 0;
        Span<int> locals = stackalloc int[9];

        var pc = 0;
        while (pc >= 0 && pc < _instructions.Count)
        {
            var op = _instructions[pc++];
            switch (op)
            {
            case >= Tiles.Man1 and <= Tiles.Man9:
                stack[stackCount++] = op - Tiles.Man1 + 1;
                break;

            case >= Tiles.Pin1 and <= Tiles.Pin9:
                stack[stackCount++] = locals[op - Tiles.Pin1];
                break;

            case >= Tiles.Sou1 and <= Tiles.Sou9:
                if (stackCount >= 1)
                {
                    locals[op - Tiles.Sou1] = stack[--stackCount];
                }
                break;

            case Tiles.East:
                if (stackCount >= 2)
                {
                    var (b, a) = (stack[--stackCount], stack[--stackCount]);
                    stack[stackCount++] = a + b;
                }
                break;

            case Tiles.South:
                if (stackCount >= 2)
                {
                    var (b, a) = (stack[--stackCount], stack[--stackCount]);
                    stack[stackCount++] = a - b;
                }
                break;

            case Tiles.West:
                if (stackCount >= 2)
                {
                    var (b, a) = (stack[--stackCount], stack[--stackCount]);
                    stack[stackCount++] = a * b;
                }
                break;

            case Tiles.North:
                if (stackCount >= 2)
                {
                    var (b, a) = (stack[--stackCount], stack[--stackCount]);
                    int q, r;
                    try
                    {
                        (q, r) = EDivRem(a, b);
                    }
                    catch (DivideByZeroException)
                    {
                        (q, r) = (0, 0);
                    }
                    catch (ArithmeticException) // Thrown when dividing 'int.MinValue' by -1 (regardless of mode).
                    {
                        (q, r) = (int.MinValue, 0);
                    }
                    stack[stackCount++] = q;
                    stack[stackCount++] = r;
                }
                break;

            case Tiles.White:
                int readByte;
                if (input is null)
                {
                    readByte = -1;
                }
                else
                {
                    readByte = input.ReadByte();
                    if (readByte == -1)
                    {
                        // Ensure we never read from this stream again even if more data becomes available.
                        input = null;
                    }
                }
                stack[stackCount++] = readByte;
                break;

            case Tiles.Green:
                if (stackCount >= 1)
                {
                    output?.WriteByte((byte)stack[--stackCount]);
                }
                break;

            case Tiles.Red:
                if (stackCount >= 2)
                {
                    var (x, t) = (stack[--stackCount], stack[--stackCount]);
                    if (x > 0)
                    {
                        pc = (EDivRem(pc, 13).Quotient + t) * 13;
                    }
                }
                break;
            }

            if (pc % 13 == 0)
            {
                stackCount = 0;
            }
        }

        return 0;
    }

    static (int Quotient, int Remainder) EDivRem(int a, int b)
    {
        var (q, r) = Math.DivRem(a, b);
        if (r < 0)
        {
            if (b > 0)
            {
                q--;
                r += b;
            }
            else
            {
                q++;
                r -= b;
            }
        }

        return (q, r);
    }

    static class CodePoints
    {
        public const int LF = 0x0A;
        public const int CR = 0x0D;

        public const int MahjongTilesStart = 0x1F000;
        public const int MahjongTilesEnd = 0x1F02F;

        public const int Man1 = 0x1F007;
        public const int Man2 = 0x1F008;
        public const int Man3 = 0x1F009;
        public const int Man4 = 0x1F00A;
        public const int Man5 = 0x1F00B;
        public const int Man6 = 0x1F00C;
        public const int Man7 = 0x1F00D;
        public const int Man8 = 0x1F00E;
        public const int Man9 = 0x1F00F;

        public const int Pin1 = 0x1F019;
        public const int Pin2 = 0x1F01A;
        public const int Pin3 = 0x1F01B;
        public const int Pin4 = 0x1F01C;
        public const int Pin5 = 0x1F01D;
        public const int Pin6 = 0x1F01E;
        public const int Pin7 = 0x1F01F;
        public const int Pin8 = 0x1F020;
        public const int Pin9 = 0x1F021;

        public const int Sou1 = 0x1F010;
        public const int Sou2 = 0x1F011;
        public const int Sou3 = 0x1F012;
        public const int Sou4 = 0x1F013;
        public const int Sou5 = 0x1F014;
        public const int Sou6 = 0x1F015;
        public const int Sou7 = 0x1F016;
        public const int Sou8 = 0x1F017;
        public const int Sou9 = 0x1F018;

        public const int East = 0x1F000;
        public const int South = 0x1F001;
        public const int West = 0x1F002;
        public const int North = 0x1F003;
        public const int White = 0x1F006;
        public const int Green = 0x1F005;
        public const int Red = 0x1F004;
    }

    static class Tiles
    {
        public const int Man1 = 0;
        public const int Man2 = 1;
        public const int Man3 = 2;
        public const int Man4 = 3;
        public const int Man5 = 4;
        public const int Man6 = 5;
        public const int Man7 = 6;
        public const int Man8 = 7;
        public const int Man9 = 8;

        public const int Pin1 = 9;
        public const int Pin2 = 10;
        public const int Pin3 = 11;
        public const int Pin4 = 12;
        public const int Pin5 = 13;
        public const int Pin6 = 14;
        public const int Pin7 = 15;
        public const int Pin8 = 16;
        public const int Pin9 = 17;

        public const int Sou1 = 18;
        public const int Sou2 = 19;
        public const int Sou3 = 20;
        public const int Sou4 = 21;
        public const int Sou5 = 22;
        public const int Sou6 = 23;
        public const int Sou7 = 24;
        public const int Sou8 = 25;
        public const int Sou9 = 26;

        public const int East = 27;
        public const int South = 28;
        public const int West = 29;
        public const int North = 30;
        public const int White = 31;
        public const int Green = 32;
        public const int Red = 33;
    }
}

[Serializable]
class ScriptException : Exception
{
    public ScriptException()
    {
    }

    public ScriptException(string message) : base(message)
    {
    }

    public ScriptException(string message, Exception inner) : base(message, inner)
    {
    }

    protected ScriptException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context
    ) : base(info, context)
    {
    }
}
