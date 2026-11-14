#include "GlpInstance.h"
#include "HAL/PlatformMisc.h"
#include "Modules/ModuleManager.h"

/**
 * The game's module. It takes the one-copy lock as it loads (0.78), seconds into the start and before the level and
 * the window: a second copy ends there, after asking the copy that runs to come forward (GlpInstance::Claim).
 */
class FGrudgeLapUEModule : public FDefaultGameModuleImpl
{
public:
	virtual void StartupModule() override
	{
		if (!GlpInstance::Claim())
		{
			FPlatformMisc::RequestExit(true);
		}
	}
};

IMPLEMENT_PRIMARY_GAME_MODULE(FGrudgeLapUEModule, GrudgeLapUE, "GrudgeLapUE");
