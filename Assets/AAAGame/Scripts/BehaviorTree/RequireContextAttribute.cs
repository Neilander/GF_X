[System.AttributeUsage(System.AttributeTargets.Class)]
public class RequireContextAttribute : System.Attribute
{
    public System.Type ContextType;
    public RequireContextAttribute(System.Type contextType)
    {
        ContextType = contextType;
    }
}