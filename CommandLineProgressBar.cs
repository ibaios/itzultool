using System;
using AssetsTools.NET;

namespace ItzulTool
{
    public partial class CommandLineProgressBar : IAssetBundleCompressProgress
    {
        float step = 5;
        float previousProgress = 0;
        public void SetProgress(float progress)
        {
            float progressInPerc = progress * 100;
            float nextProgressToDisplay = previousProgress + step;
            if(progressInPerc >= nextProgressToDisplay)
            {
                previousProgress = previousProgress + 5;
                Console.WriteLine("%" + previousProgress);
            }
            
        }

    }
}