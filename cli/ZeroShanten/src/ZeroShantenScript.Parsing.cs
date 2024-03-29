using System.Diagnostics;

namespace ZeroShanten;

public partial class ZeroShantenScript
{
    public static ZeroShantenScript Parse(string source)
    {
        var instructionCount = GetInstructionCount(source, out var parseErrors);
        if (parseErrors is not null)
        {
            return new ZeroShantenScript(parseErrors.AsReadOnly());
        }

        var instructions = GetInstructions(source, instructionCount);

        return new ZeroShantenScript(instructions);
    }

    static int GetInstructionCount(string source, out List<ParseError>? parseErrors)
    {
        var instructionCount = 0;
        parseErrors = null;

        var line = 1;
        var character = 1;
        var cr = false;
        var commentedOut = false;

        var currentHandTileCounts = (stackalloc int[35]); // 34 for supported tiles, 1 for unsupported tiles.
        var currentHandTotalTileCount = 0;
        var currentHandStartLine = line;
        var currentHandStartCharacter = character;

        var runeEnumerator = source.EnumerateRunes();
        while (runeEnumerator.MoveNext())
        {
            var codePoint = runeEnumerator.Current.Value;
            switch (codePoint)
            {
            case CodePoints.Lf:
                if (!cr)
                {
                    line++;
                    character = 1;
                }
                cr = false;
                commentedOut = false;
                break;
            case CodePoints.Cr:
                line++;
                character++;
                cr = true;
                commentedOut = false;
                break;
            case >= CodePoints.MahjongTilesFirst and <= CodePoints.MahjongTilesLast when !commentedOut:
                if (codePoint is CodePoints.MahjongTileBack)
                {
                    commentedOut = true;
                    goto default;
                }

                var kindIndex = codePoint switch
                {
                    >= CodePoints.Man1 and <= CodePoints.Man9 => codePoint - CodePoints.Man1 + 0,
                    >= CodePoints.Pin1 and <= CodePoints.Pin9 => codePoint - CodePoints.Pin1 + 9,
                    >= CodePoints.Sou1 and <= CodePoints.Sou9 => codePoint - CodePoints.Sou1 + 18,
                    >= CodePoints.East and <= CodePoints.North => codePoint - CodePoints.East + 27,
                    >= CodePoints.Red and <= CodePoints.White => -codePoint + CodePoints.White + 31,
                    _ => 34,
                };
                currentHandTileCounts[kindIndex]++;
                currentHandTotalTileCount++;

                if (currentHandTotalTileCount == 1)
                {
                    currentHandStartLine = line;
                    currentHandStartCharacter = character;
                }
                else if (currentHandTotalTileCount == 13)
                {
                    var canCheckShanten = true;

                    foreach (var tileCount in currentHandTileCounts[..34])
                    {
                        if (tileCount > 4)
                        {
                            (parseErrors ??= new List<ParseError>()).Add(new ParseError(
                                ParseErrorCode.HandContainsMoreThanFourCopiesOfATile,
                                currentHandStartLine, currentHandStartCharacter,
                                line, character));
                            canCheckShanten = false;
                            break;
                        }
                    }
                    if (currentHandTileCounts[34] != 0)
                    {
                        (parseErrors ??= new List<ParseError>()).Add(new ParseError(
                            ParseErrorCode.HandContainsUnsupportedTiles,
                            currentHandStartLine, currentHandStartCharacter,
                            line, character));
                        canCheckShanten = false;
                    }

                    if (canCheckShanten)
                    {
                        var shanten = Shanten.GetShanten(currentHandTileCounts[..34]);
                        if (shanten != 0)
                        {
                            (parseErrors ??= new List<ParseError>()).Add(new ParseError(
                                (ParseErrorCode)shanten,
                                currentHandStartLine, currentHandStartCharacter,
                                line, character));
                        }
                    }

                    currentHandTileCounts.Clear();
                    currentHandTotalTileCount = 0;
                }

                instructionCount++;
                goto default;
            default:
                character++;
                cr = false;
                break;
            }
        }
        character++;

        // We only report shanten errors when the instruction count is a multiple of 13 in order to improve the user
        // experience when the user has set up this tool to continually check for errors while they are typing. Doing
        // otherwise would result in a cascade of shanten errors when the user is making modifications anywhere but
        // near the very end of the script, which wouldn't be very helpful.
        if (instructionCount % 13 != 0)
        {
            if (parseErrors is null)
            {
                parseErrors = new List<ParseError>(1);
            }
            else
            {
                parseErrors.Clear();
            }
            parseErrors.Add(new ParseError(
                ParseErrorCode.InstructionCountIsNotAMultipleOf13,
                line, character,
                line, character));
        }

        return instructionCount;
    }

    static byte[] GetInstructions(string source, int instructionCount)
    {
        Debug.Assert(instructionCount % 13 == 0, "Instruction count is not a multiple of 13.");

        var instructions = new byte[instructionCount];

        var index = 0;
        var commentedOut = false;

        var runeEnumerator = source.EnumerateRunes();
        while (runeEnumerator.MoveNext())
        {
            var codePoint = runeEnumerator.Current.Value;
            switch (codePoint)
            {
            case CodePoints.Lf:
            case CodePoints.Cr:
                commentedOut = false;
                break;
            case >= CodePoints.MahjongTilesFirst and <= CodePoints.MahjongTilesLast when !commentedOut:
                if (codePoint is CodePoints.MahjongTileBack)
                {
                    commentedOut = true;
                    break;
                }
                instructions[index++] = (byte)(codePoint switch
                {
                    >= CodePoints.Man1 and <= CodePoints.Man9 => codePoint - CodePoints.Man1 + Instructions.Rc1,
                    >= CodePoints.Pin1 and <= CodePoints.Pin9 => codePoint - CodePoints.Pin1 + Instructions.Rv1,
                    >= CodePoints.Sou1 and <= CodePoints.Sou9 => codePoint - CodePoints.Sou1 + Instructions.Wv1,
                    >= CodePoints.East and <= CodePoints.North => codePoint - CodePoints.East + Instructions.Add,
                    >= CodePoints.Red and <= CodePoints.White => -codePoint + CodePoints.White + Instructions.Rio,
                    _ => 0,
                });
                break;
            }
        }

        Debug.Assert(index == instructions.Length, "Too few or too many instructions retrieved.");

        return instructions;
    }

    static class CodePoints
    {
        public const int Lf = 0x0A;
        public const int Cr = 0x0D;

        public const int MahjongTilesFirst = 0x1F000;
        public const int MahjongTilesLast = 0x1F02F;

        public const int MahjongTileBack = 0x1F02B;

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
}

public readonly record struct ParseError(
    ParseErrorCode Code,
    int Line,
    int Character,
    int EndLine,
    int EndCharacter);

public enum ParseErrorCode
{
    InstructionCountIsNotAMultipleOf13 = 0,
    HandIs1Shanten = 1,
    HandIs2Shanten = 2,
    HandIs3Shanten = 3,
    HandIs4Shanten = 4,
    HandIs5Shanten = 5,
    HandIs6Shanten = 6,
    HandContainsMoreThanFourCopiesOfATile = 7,
    HandContainsUnsupportedTiles = 8,
}
