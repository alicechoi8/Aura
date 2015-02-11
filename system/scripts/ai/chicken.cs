//--- Aura Script -----------------------------------------------------------
//  Chicken AI
//--- Description -----------------------------------------------------------
//  AI for chickens.
//---------------------------------------------------------------------------

public class ChickenAi : AiScript
{
	public ChickenAi()
	{
		SetAggroRadius(100000);
		
		Hates("/pc/");
	
	}
	
	protected override IEnumerable Idle()
	{
		Do(Wander());
		Do(Wait(8000));
		Do(StartSkill(SkillId.Rest));
		Do(Wait(2000, 30000));
		Do(StopSkill(SkillId.Rest));
	}
	
	protected override IEnumerable Aggro()
	{
		Do(Attack(3));
		Do(Wait(3000));
	}
	
	protected override IEnumerable Love()
	{
		Do(Follow(300));
		Do(Wait(5000, 10000));
	}
}
