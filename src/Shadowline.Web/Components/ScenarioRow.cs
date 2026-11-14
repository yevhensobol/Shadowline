using Shadowline.Core.Metrics;
using Shadowline.Core.Sites;

namespace Shadowline.Web.Components;

public sealed record ScenarioRow(Site Site, DateTimeOffset Start, int WindowHours, WindowMetrics Metrics);
