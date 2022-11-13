using System.Collections.ObjectModel;
using System.Diagnostics;

namespace ZeroShanten;

public partial class ZeroShantenScript
{
    static readonly ReadOnlyCollection<ParseError> s_noParseErrors = new(Array.Empty<ParseError>());

    readonly byte[] _instructions;
    readonly ReadOnlyCollection<ParseError> _parseErrors;

    ZeroShantenScript(byte[] instructions)
    {
        Debug.Assert(instructions.Length % 13 == 0, "Instruction count is not a multiple of 13.");

        _instructions = instructions;
        _parseErrors = s_noParseErrors;
    }

    ZeroShantenScript(ReadOnlyCollection<ParseError> parseErrors)
    {
        Debug.Assert(parseErrors.Count != 0, "Parse error list is empty.");

        _instructions = Array.Empty<byte>();
        _parseErrors = parseErrors;
    }

    public IReadOnlyList<ParseError> ParseErrors => _parseErrors;

    public bool IsValid => _parseErrors.Count == 0;

    public void Invoke(Stream? input = null, Stream? output = null)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("The script is invalid and can not be invoked.");
        }

        var variables = (stackalloc int[9]);
        var stack = (stackalloc int[13]);
        var stackTop = 0;
        var pc = 0;
        while ((uint)pc < (uint)_instructions.Length)
        {
            var instruction = _instructions[pc++];
            switch (instruction)
            {
            case >= Instructions.Rc1 and <= Instructions.Rc9:
                stack[stackTop++] = instruction - Instructions.Rc1 + 1;
                break;
            case >= Instructions.Rv1 and <= Instructions.Rv9:
                stack[stackTop++] = variables[instruction - Instructions.Rv1];
                break;
            case >= Instructions.Wv1 and <= Instructions.Wv9:
                if (stackTop >= 1)
                {
                    variables[instruction - Instructions.Wv1] = stack[--stackTop];
                }
                break;
            case Instructions.Add:
                if (stackTop >= 2)
                {
                    var (b, a) = (stack[--stackTop], stack[--stackTop]);
                    stack[stackTop++] = a + b;
                }
                break;
            case Instructions.Sub:
                if (stackTop >= 2)
                {
                    var (b, a) = (stack[--stackTop], stack[--stackTop]);
                    stack[stackTop++] = a - b;
                }
                break;
            case Instructions.Mul:
                if (stackTop >= 2)
                {
                    var (b, a) = (stack[--stackTop], stack[--stackTop]);
                    stack[stackTop++] = a * b;
                }
                break;
            case Instructions.Div:
                if (stackTop >= 2)
                {
                    var (b, a) = (stack[--stackTop], stack[--stackTop]);
                    int q, r;
                    try
                    {
                        // Euclidean division.
                        (q, r) = Math.DivRem(a, b);
                        if (r < 0)
                        {
                            if (b >= 0)
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
                    }
                    catch (DivideByZeroException)
                    {
                        (q, r) = (0, 0);
                    }
                    catch (ArithmeticException) // Always thrown when dividing 'int.MinValue' by -1.
                    {
                        (q, r) = (int.MinValue, 0);
                    }
                    stack[stackTop++] = q;
                    stack[stackTop++] = r;
                }
                break;
            case Instructions.Rio:
                stack[stackTop++] = input?.ReadByte() ?? -1;
                break;
            case Instructions.Wio:
                if (stackTop >= 1)
                {
                    output?.WriteByte((byte)stack[--stackTop]);
                }
                break;
            case Instructions.Jgz:
                if (stackTop >= 2)
                {
                    var (t, x) = (stack[--stackTop], stack[--stackTop]);
                    if (x > 0)
                    {
                        pc = (pc / 13 + t) * 13;
                    }
                }
                break;
            default:
                Debug.Fail($"Unknown instruction: {instruction}");
                break;
            }

            if (pc % 13 == 0)
            {
                stackTop = 0;
            }
        }
    }

    static class Instructions
    {
        public const int Rc1 = 1;
        public const int Rc2 = 2;
        public const int Rc3 = 3;
        public const int Rc4 = 4;
        public const int Rc5 = 5;
        public const int Rc6 = 6;
        public const int Rc7 = 7;
        public const int Rc8 = 8;
        public const int Rc9 = 9;

        public const int Rv1 = 10;
        public const int Rv2 = 11;
        public const int Rv3 = 12;
        public const int Rv4 = 13;
        public const int Rv5 = 14;
        public const int Rv6 = 15;
        public const int Rv7 = 16;
        public const int Rv8 = 17;
        public const int Rv9 = 18;

        public const int Wv1 = 19;
        public const int Wv2 = 20;
        public const int Wv3 = 21;
        public const int Wv4 = 22;
        public const int Wv5 = 23;
        public const int Wv6 = 24;
        public const int Wv7 = 25;
        public const int Wv8 = 26;
        public const int Wv9 = 27;

        public const int Add = 28;
        public const int Sub = 29;
        public const int Mul = 30;
        public const int Div = 31;
        public const int Rio = 32;
        public const int Wio = 33;
        public const int Jgz = 34;
    }
}
