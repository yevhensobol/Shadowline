namespace Shadowline.Core.Replan;

public interface IReplanner
{
    ReplanResult Plan(ReplanRequest request);
}
