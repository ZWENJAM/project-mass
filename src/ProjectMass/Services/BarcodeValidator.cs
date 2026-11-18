namespace ProjectMass.Services;

public static class BarcodeValidator
{
    public static bool IsValid(string? code)
    {
        if (code is null || code.Length is < 8 or > 14 || code.Any(c => c is < '0' or > '9')) return false;
        return HasCheckDigit(code) || (code.Length == 8 && code[0] is '0' or '1' && HasCheckDigit(ExpandUpcE(code)));
    }
    private static bool HasCheckDigit(string code)
    {
        var sum = 0;
        for (var i = code.Length - 2; i >= 0; i--) sum += (code[i] - '0') * ((code.Length - 2 - i) % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == code[^1] - '0';
    }
    public static string Normalize(string code) => code.Length == 8 && code.All(char.IsAsciiDigit) && !HasCheckDigit(code) && code[0] is '0' or '1' && HasCheckDigit(ExpandUpcE(code)) ? ExpandUpcE(code) : code;
    public static string Canonical(string code) => Normalize(code).TrimStart('0');
    private static string ExpandUpcE(string code) => code[6] switch
    {
        '0' or '1' or '2' => $"{code[..3]}{code[6]}0000{code[3..6]}{code[7]}",
        '3' => $"{code[..4]}00000{code[4..6]}{code[7]}",
        '4' => $"{code[..5]}00000{code[5]}{code[7]}",
        _ => $"{code[..6]}0000{code[6..]}"
    };
}
