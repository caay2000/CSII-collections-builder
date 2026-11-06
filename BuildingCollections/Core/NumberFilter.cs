namespace BuildingCollections.Core
{
    public enum CompareOp
    {
        Equal,
        NotEqual,
        Greater,
        Less,
    }

    public struct NumberFilter
    {
        public CompareOp Op;

        public int? Value;

        public static NumberFilter All => new NumberFilter { Op = CompareOp.Equal, Value = null };

        public static NumberFilter Of(CompareOp op, int value) => new NumberFilter { Op = op, Value = value };

        public bool Matches(int number)
        {
            if (!Value.HasValue)
            {
                return true;
            }

            switch (Op)
            {
                case CompareOp.Equal: return number == Value.Value;
                case CompareOp.NotEqual: return number != Value.Value;
                case CompareOp.Greater: return number > Value.Value;
                case CompareOp.Less: return number < Value.Value;
                default: return true;
            }
        }
    }
}
