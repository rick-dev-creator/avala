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
        private bool started;
        private bool fresh = true;
        private bool dotted;

        public bool Expands(char character)
        {
            if (escaped)
            {
                escaped = false;

                return Literal(character, protectedCharacter: true);
            }

            if (single || quoted)
            {
                return Quoted(character);
            }

            (single, quoted, escaped) = (character == '\'', character == '"', character == '\\');

            return !single && !quoted && !escaped && Literal(character, protectedCharacter: false);
        }

        private bool Quoted(char character)
        {
            if ((single && character == '\'') || (quoted && character == '"'))
            {
                (single, quoted) = (false, false);

                return false;
            }

            escaped = quoted && character == '\\';

            return !escaped && Literal(character, protectedCharacter: true);
        }

        private bool Literal(char character, bool protectedCharacter)
        {
            var expands = !protectedCharacter && (character == '{' || (character is '*' or '?' or '[' && (!started || dotted)));
            dotted = fresh ? character == '.' : dotted && character != '/';
            (started, fresh) = (true, character == '/');

            return expands;
        }
    }
}
