using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Licensing
{
    public interface IFeatureGate
    {
        FeatureGateResult Check(PremiumFeature feature);
        FeatureGateResult Require(PremiumFeature feature);
        Task<FeatureGateResult> CheckAsync(PremiumFeature feature, CancellationToken cancellationToken = default);
    }
}
