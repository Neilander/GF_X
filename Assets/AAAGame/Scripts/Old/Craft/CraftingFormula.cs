public class CraftingFormula
{
    public StringIntPair[] RequiredItems;
    public StringIntPair[] ProducedItems;
    public Fix64 Workload;
    public CraftingFormula() { }
    public CraftingFormula(StringIntPair[] requiredItems, StringIntPair[] producedItems, Fix64 workload)
        => (RequiredItems, ProducedItems, Workload) = (requiredItems, producedItems, workload);
}