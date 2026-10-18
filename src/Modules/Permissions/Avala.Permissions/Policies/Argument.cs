namespace Avala.Permissions.Policies;

internal readonly record struct Argument(string Value, string Raw)
{
    public bool Expands
    {
        get
        {
            var reading = new ShellWordReading();

            return Raw.Any(reading.Expands);
        }
    }

    private sealed class ShellWordReading
    {
        private bool single;
        private bool quoted;
        private bool escaped;
        private bool braced;
        private char previous;

        public bool Expands(char character)
        {
            if (escaped)
            {
                (escaped, previous) = (false, '\0');

                return false;
            }

            if (single)
            {
                single = character != '\'';

                return false;
            }

            if (quoted)
            {
                (quoted, escaped) = (character != '"', character == '\\');

                return character == '$';
            }

            return Unquoted(character);
        }

        private bool Unquoted(char character)
        {
            (single, quoted, escaped) = (character == '\'', character == '"', character == '\\');
            var expands = character is '$' or '*' or '?' or '['
                || (braced && (character == ',' || (character == '.' && previous == '.')));
            braced = character == '{' || (braced && character != '}');
            previous = character;

            return expands;
        }
    }
}
