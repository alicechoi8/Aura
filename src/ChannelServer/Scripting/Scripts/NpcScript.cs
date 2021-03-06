﻿// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Aura.Channel.Database;
using Aura.Channel.Network.Sending;
using Aura.Channel.World;
using Aura.Channel.World.Entities;
using Aura.Data;
using Aura.Shared.Database;
using Aura.Shared.Mabi.Const;
using Aura.Shared.Util;
using Aura.Channel.World.Inventory;

namespace Aura.Channel.Scripting.Scripts
{
	public abstract class NpcScript : GeneralScript
	{
		private string _response;
		private SemaphoreSlim _resumeSignal;
		private CancellationTokenSource _cancellation;

		public ConversationState ConversationState { get; private set; }

		/// <summary>
		/// The NPC associated with this instance of the NPC script.
		/// </summary>
		public NPC NPC { get; set; }

		private Creature _player;
		/// <summary>
		/// The player associated with this instance of the NPC script.
		/// </summary>
		public Creature Player
		{
			get
			{
				if (_player == null)
					throw new Exception("NpcScript: Missing player in " + this.GetType().Name);
				return _player;
			}
			set { _player = value; }
		}

		/// <summary>
		/// Gets or sets how well the NPC remembers the player.
		/// </summary>
		public int Memory
		{
			get { return this.NPC.GetMemory(this.Player); }
			set { this.NPC.SetMemory(this.Player, value); }
		}

		/// <summary>
		/// Gets or sets how much the NPC likes the player.
		/// </summary>
		public int Favor
		{
			get { return this.NPC.GetFavor(this.Player); }
			set { this.NPC.SetFavor(this.Player, value); }
		}

		/// <summary>
		/// Gets or sets how much the player stresses the NPC.
		/// </summary>
		public int Stress
		{
			get { return this.NPC.GetStress(this.Player); }
			set { this.NPC.SetStress(this.Player, value); }
		}

		/// <summary>
		/// Returns the player's current title.
		/// </summary>
		public int Title
		{
			get { return this.Player.Titles.SelectedTitle; }
		}

		/// <summary>
		/// Gets and set the player's amount of gold,
		/// by modifying the inventory.
		/// </summary>
		public int Gold
		{
			get { return this.Player.Inventory.Gold; }
			set { this.Player.Inventory.Gold = value; }
		}

		/// <summary>
		/// Initializes class
		/// </summary>
		protected NpcScript()
		{
			_resumeSignal = new SemaphoreSlim(0);
			_cancellation = new CancellationTokenSource();
		}

		/// <summary>
		/// Initiates the NPC script, creating and placing the NPC.
		/// </summary>
		/// <returns></returns>
		public override bool Init()
		{
			this.NPC = new NPC();
			this.NPC.State = CreatureStates.Npc | CreatureStates.NamedNpc | CreatureStates.GoodNpc;
			this.NPC.ScriptType = this.GetType();
			this.NPC.AI = ChannelServer.Instance.ScriptManager.GetAi("npc_normal", this.NPC);

			// Load script first, to get race set and stuff, then load the NPC data.
			this.Load();
			this.NPC.LoadDefault();

			if (this.NPC.RegionId > 0)
			{
				var region = ChannelServer.Instance.World.GetRegion(this.NPC.RegionId);
				if (region == null)
				{
					Log.Error("Failed to spawn '{0}', region '{1}' not found.", this.GetType().Name, this.NPC.RegionId);
					return false;
				}

				region.AddCreature(this.NPC);
			}

			this.NPC.SpawnLocation = new Location(this.NPC.RegionId, this.NPC.GetPosition());

			return true;
		}

		// ------------------------------------------------------------------

		/// <summary>
		/// Called from packet handler when a player starts the conversation.
		/// </summary>
		public virtual async void TalkAsync()
		{
			this.ConversationState = ConversationState.Ongoing;
			try
			{
				if (!this.Player.IsPet)
					await this.Talk();
				else
					await this.TalkPet();
			}
			catch (OperationCanceledException)
			{
				// Thrown to get out of the async chain
			}
			this.ConversationState = ConversationState.Ended;
		}

		/// <summary>
		/// Called when a player starts the conversation.
		/// </summary>
		protected virtual async Task Talk()
		{
			await Task.Yield();
		}

		/// <summary>
		/// Called when a pet starts the conversation.
		/// </summary>
		protected virtual async Task TalkPet()
		{
			// Officials don't use random messages, but one message for every NPC,
			// which is usually the default one below. However, some NPCs have a
			// different message, ones added later in particular, so we'll just
			// RNG it for the default message, less overriding for something
			// nobody cares about.

			switch (this.Random(3))
			{
				default:
					if (this.NPC.IsMale)
					{
						this.Close(Hide.None, "(I don't think he can understand me)");
						break;
					}
					else if (this.NPC.IsFemale)
					{
						this.Close(Hide.None, "(I don't think she can understand me)");
						break;
					}

					// Go to next case if gender isn't clear
					goto case 1;

				case 1: this.Close(Hide.None, "(This conversation doesn't seem to be going anywhere.)"); break;
				case 2: this.Close(Hide.None, "(I don't think we'll see things eye to eye.)"); break;
			}

			// Messages for "things", like book shelves.
			//this.Close("<title name='NONE'/>(I don't think I can talk to this.)");
			//this.Close("<title name='NONE'/>(I don't think we'll see things eye to eye.)");

			await Task.Yield();
		}

		/// <summary>
		/// Called from packet handler when a player starts the conversation with a gift.
		/// </summary>
		public virtual async void GiftAsync(Item gift)
		{
			this.ConversationState = ConversationState.Ongoing;
			try
			{
				var score = this.GetGiftReaction(gift);

				await this.Gift(gift, score);
			}
			catch (OperationCanceledException)
			{
				// Thrown to get out of the async chain
			}
			this.ConversationState = ConversationState.Ended;
		}

		/// <summary>
		/// Called from Gift, override to react to gifts.
		/// </summary>
		/// <param name="gift">Item gifted to the NPC by the player.</param>
		/// <param name="reaction">NPCs reaction to the gift.</param>
		/// <returns></returns>
		protected virtual async Task Gift(Item gift, GiftReaction reaction)
		{
			this.Msg("Thank you.");

			await Task.Yield();
		}

		/// <summary>
		/// Returns NPCs reaction to gifted item.
		/// </summary>
		/// <param name="gift"></param>
		/// <returns></returns>
		protected virtual GiftReaction GetGiftReaction(Item gift)
		{
			var score = this.NPC.GiftWeights.CalculateScore(gift);

			if (gift.Info.Id == 51046) // Likeability pot
			{
				score = 10;
				this.Favor += 10; // Determined through a LOT of pots... RIP my bank :(
				this.Memory += 4; // Gotta remember who gave you roofies!!
			}
			else
			{
				var delta = score;

				if (gift.Data.StackType == Data.Database.StackType.Stackable)
				{
					delta *= gift.Amount * gift.Data.StackMax * 3;
					delta /= (1 + 2 * (Random(4) + 7));
				}
				else
				{
					delta *= 3;
					delta /= (Random(7) + 6);
				}

				this.Favor += delta;
			}

			// Reduce stress by 0 ~ score (or at least 4) - 1 for good gifts
			if (score >= 0)
				this.Stress -= this.Random(Math.Max(4, score));

			if (score > 6)
				return GiftReaction.Love;
			if (score > 3)
				return GiftReaction.Like;
			if (score > -4)
				return GiftReaction.Neutral;
			else
				return GiftReaction.Dislike;
		}

		/// <summary>
		/// Sends Close, using either the message or the standard ending phrase.
		/// </summary>
		/// <param name="response"></param>
		public void End(string message = null)
		{
			this.Close(message ?? "(You ended your conversation with <npcname/>.)");
		}

		/// <summary>
		/// Sets response and returns from Select.
		/// </summary>
		/// <param name="response"></param>
		public void Resume(string response)
		{
			_response = response;
			_resumeSignal.Release();
		}

		/// <summary>
		/// Cancels conversation.
		/// </summary>
		public void Cancel()
		{
			_cancellation.Cancel();
		}

		/// <summary>
		/// Greets the player. **MODIFIES STATS**
		/// </summary>
		protected virtual void Greet()
		{
			// TODO: if (DoingPtj()) ...

			var memory = this.Memory;
			var stress = this.Stress;

			if (memory <= 0)
			{
				this.Memory = 1;
			}
			else if (memory == 1)
			{
				// Do nothing. Keeps players from raising their familiarity
				// just by talking.
			}
			else if (memory <= 6 && stress == 0)
			{
				this.Memory += 1;
				this.Stress += 5;
			}
			else if (stress == 0)
			{
				this.Memory += 1;
				this.Stress += 10;
			}

			var msg = Localization.Get("(No greeting messages defined.)");

			// Take the highest greeting without going over their memory
			foreach (var list in this.NPC.Greetings.TakeWhile(k => k.Key <= memory))
			{
				var msgs = list.Value;
				msg = msgs[Random(msgs.Count)];
			}

			// Show relation values to devCATs for debugging
			if (this.Player.Titles.SelectedTitle == 60001)
			{
				msg += "<br/>" + "Favor: " + this.Favor;
				msg += "<br/>" + "Memory: " + this.Memory;
				msg += "<br/>" + "Stress: " + this.Stress;
			}

			this.Msg(Hide.None, msg, FavorExpression());
		}

		/// <summary>
		/// Gets the mood.
		/// </summary>
		/// <returns></returns>
		public virtual NpcMood GetMood()
		{
			int stress = this.Stress;
			int favor = this.Favor;
			int memory = this.Memory;

			if (stress > 12)
				return NpcMood.VeryStressed;
			if (stress > 8)
				return NpcMood.Stressed;
			if (favor > 40)
				return NpcMood.Love;
			if (favor > 30)
				return NpcMood.ReallyLikes;
			if (favor > 10)
				return NpcMood.Likes;
			if (favor < -20)
				return NpcMood.Hates;
			if (favor < -10)
				return NpcMood.ReallyDislikes;
			if (favor < -5)
				return NpcMood.Dislikes;

			if (memory > 15)
				return NpcMood.BestFriends;
			if (memory > 5)
				return NpcMood.Friends;

			return NpcMood.Neutral;

		}

		/// <summary>
		/// Gets the mood string for the current mood.
		/// </summary>
		/// <returns></returns>
		public string GetMoodString()
		{
			return this.GetMoodString(this.GetMood());
		}

		/// <summary>
		/// Gets the mood string for the given mood.
		/// </summary>
		/// <param name="mood">The mood.</param>
		/// <returns></returns>
		public virtual string GetMoodString(NpcMood mood)
		{
			string moodStr;

			switch (mood)
			{
				case NpcMood.VeryStressed:
					moodStr = Localization.Get("(<npcname/> is giving me and impression that I am interruping something.)");
					break;

				case NpcMood.Stressed:
					moodStr = Localization.Get("(<npcname/> is giving me a look that it may be better to stop this conversation.)");
					break;

				case NpcMood.BestFriends:
					moodStr = Localization.Get("(<npcname/> is smiling at me as if we've known each other for years.)");
					break;

				case NpcMood.Friends:
					moodStr = Localization.Get("(<npcname/> is really giving me a friendly vibe.)");
					break;

				case NpcMood.Hates:
					moodStr = this.RndStr(
						Localization.Get("(<npcname/> is looking at me like they don't want to see me.)"),
						Localization.Get("(<npcname/> obviously hates me.)")
					);
					break;

				case NpcMood.ReallyDislikes:
					moodStr = Localization.Get("(<npcname/> is looking at me with obvious disgust.)");
					break;

				case NpcMood.Dislikes:
					moodStr = Localization.Get("(<npcname/> looks like it's a bit unpleasent that I'm here.)");
					break;

				case NpcMood.Likes:
					moodStr = Localization.Get("(<npcname/> is looking at me with great interest.)");
					break;

				case NpcMood.ReallyLikes:
					moodStr = Localization.Get("(<npcname/> is giving me a friendly smile.)");
					break;

				case NpcMood.Love:
					moodStr = Localization.Get("(<npcname/> is giving me a welcome look.)");
					break;

				default:
					moodStr = this.RndStr(
						Localization.Get("(<npcname/> is looking at me.)"),
						Localization.Get("(<npcname/> is looking in my direction.)"),
						Localization.Get("(<npcname/> is waiting for me to says something.)"),
						Localization.Get("(<npcname/> is paying attention to me.)")
					);
					break;
			}

			// (<npcname/> is slowly looking me over.)

			return moodStr;
		}

		/// <summary>
		/// Conversation (keywords) loop with initial mood message.
		/// </summary>
		/// <returns></returns>
		public virtual async Task StartConversation()
		{
			// Show mood once at the start of the conversation
			this.Msg(Hide.Name, this.GetMoodString(), this.FavorExpression());

			await Conversation();
		}

		/// <summary>
		/// Conversation (keywords) loop.
		/// </summary>
		/// <remarks>
		/// This is a separate method so it can be called from hooks
		/// that go into keyword handling after they're done,
		/// without mood message.
		/// </remarks>
		/// <returns></returns>
		public virtual async Task Conversation()
		{
			// Infinite keyword handling until End is clicked.
			while (true)
			{
				this.ShowKeywords();
				var keyword = await Select();

				if (keyword == "@end")
					break;

				await Hook("before_keywords", keyword);

				await this.Keywords(keyword);
			}
		}

		/// <summary>
		/// Called from conversation, keyword handling.
		/// </summary>
		/// <param name="keyword"></param>
		/// <returns></returns>
		protected virtual async Task Keywords(string keyword)
		{
			await Task.Yield();
		}

		/// <summary>
		/// Modifies memory, favor, and stress and sends random reaction
		/// message based on the favor change.
		/// </summary>
		/// <param name="memory"></param>
		/// <param name="favor"></param>
		/// <param name="stress"></param>
		protected virtual void ModifyRelation(int memory, int favor, int stress)
		{
			if (memory != 0) this.Memory += memory;
			if (favor != 0) this.Favor += favor;
			if (stress != 0) this.Stress += stress;

			// Seem to be multiple levels? -5, -2, 0, 2, 5?

			var msg = this.RndStr(
				Localization.Get("(I think I left a good impression.)"),
				Localization.Get("(The conversation drew a lot of interest.)"),
				Localization.Get("(That was a great conversation!)")
				// (It seems I left quite a good impression.)
			);

			this.Msg(Hide.Name, FavorExpression(), msg);
		}

		// Setup
		// ------------------------------------------------------------------		

		/// <summary>
		/// Sets the gift weights.
		/// </summary>
		/// <param name="adult">How much the NPC likes "adult" items.</param>
		/// <param name="anime">How much the NPC likes "anime" items.</param>
		/// <param name="beauty">How much the NPC likes "beauty" items.</param>
		/// <param name="individuality">How much the NPC likes "indiv" items.</param>
		/// <param name="luxury">How much the NPC likes "luxury" items.</param>
		/// <param name="maniac">How much the NPC likes "maniac" items.</param>
		/// <param name="meaning">How much the NPC likes "meaning" items.</param>
		/// <param name="rarity">How much the NPC likes "rarity" items.</param>
		/// <param name="sexy">How much the NPC likes "sexy" items.</param>
		/// <param name="toughness">How much the NPC likes "toughness" items.</param>
		/// <param name="utility">How much the NPC likes "utility" items.</param>
		protected void SetGiftWeights(float adult, float anime, float beauty, float individuality, float luxury, float maniac, float meaning, float rarity, float sexy, float toughness, float utility)
		{
			this.NPC.GiftWeights.Adult = adult;
			this.NPC.GiftWeights.Anime = anime;
			this.NPC.GiftWeights.Beauty = beauty;
			this.NPC.GiftWeights.Individuality = individuality;
			this.NPC.GiftWeights.Luxury = luxury;
			this.NPC.GiftWeights.Maniac = maniac;
			this.NPC.GiftWeights.Meaning = meaning;
			this.NPC.GiftWeights.Rarity = rarity;
			this.NPC.GiftWeights.Sexy = sexy;
			this.NPC.GiftWeights.Toughness = toughness;
			this.NPC.GiftWeights.Utility = utility;
		}

		/// <summary>
		/// Sets NPC's name.
		/// </summary>
		/// <param name="name"></param>
		protected void SetName(string name)
		{
			this.NPC.Name = name;
		}

		/// <summary>
		/// Sets NPC's portrait.
		/// </summary>
		/// <param name="name"></param>
		protected void SetPortrait(string name)
		{
			this.NPC.DialogPortrait = name;
		}

		/// <summary>
		/// Sets NPC's location.
		/// </summary>
		/// <param name="regionId"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="direction"></param>
		protected void SetLocation(int regionId, int x, int y, byte direction = 0)
		{
			this.NPC.SetLocation(regionId, x, y);
			this.NPC.Direction = direction;
		}

		/// <summary>
		/// Sets NPC's race.
		/// </summary>
		/// <param name="raceId"></param>
		protected void SetRace(int raceId)
		{
			this.NPC.Race = raceId;
		}

		/// <summary>
		/// Sets NPC's body proportions.
		/// </summary>
		/// <param name="height"></param>
		/// <param name="weight"></param>
		/// <param name="upper"></param>
		/// <param name="lower"></param>
		protected void SetBody(float height = 1, float weight = 1, float upper = 1, float lower = 1)
		{
			this.NPC.Height = height;
			this.NPC.Weight = weight;
			this.NPC.Upper = upper;
			this.NPC.Lower = lower;
		}

		/// <summary>
		/// Sets NPC's face values.
		/// </summary>
		/// <param name="skinColor"></param>
		/// <param name="eyeType"></param>
		/// <param name="eyeColor"></param>
		/// <param name="mouthType"></param>
		protected void SetFace(byte skinColor = 0, short eyeType = 0, byte eyeColor = 0, byte mouthType = 0)
		{
			this.NPC.SkinColor = skinColor;
			this.NPC.EyeType = eyeType;
			this.NPC.EyeColor = eyeColor;
			this.NPC.MouthType = mouthType;
		}

		/// <summary>
		/// Sets NPC's color values.
		/// </summary>
		/// <param name="color1"></param>
		/// <param name="color2"></param>
		/// <param name="color3"></param>
		protected void SetColor(uint color1 = 0x808080, uint color2 = 0x808080, uint color3 = 0x808080)
		{
			this.NPC.Color1 = color1;
			this.NPC.Color2 = color2;
			this.NPC.Color3 = color3;
		}

		/// <summary>
		/// Sets NPC's stand style.
		/// </summary>
		/// <param name="stand"></param>
		/// <param name="talkStand"></param>
		protected void SetStand(string stand, string talkStand = null)
		{
			this.NPC.StandStyle = stand;
			this.NPC.StandStyleTalking = talkStand;
		}

		/// <summary>
		/// Adds item to NPC's inventory.
		/// </summary>
		/// <param name="pocket"></param>
		/// <param name="itemId"></param>
		/// <param name="color1"></param>
		/// <param name="color2"></param>
		/// <param name="color3"></param>
		/// <param name="state">For robes and helmets</param>
		protected void EquipItem(Pocket pocket, int itemId, uint color1 = 0, uint color2 = 0, uint color3 = 0, ItemState state = ItemState.Up)
		{
			if (!pocket.IsEquip())
			{
				Log.Error("Pocket '{0}' is not for equipment ({1})", pocket, this.GetType().Name);
				return;
			}

			if (!AuraData.ItemDb.Exists(itemId))
			{
				Log.Error("Unknown item '{0}' ({1})", itemId, this.GetType().Name);
				return;
			}

			var item = new Item(itemId);
			item.Info.Pocket = pocket;
			item.Info.Color1 = color1;
			item.Info.Color2 = color2;
			item.Info.Color3 = color3;
			item.Info.State = (byte)state;

			this.NPC.Inventory.InitAdd(item);
		}

		/// <summary>
		/// Adds phrase to AI.
		/// </summary>
		/// <param name="phrase"></param>
		protected void AddPhrase(string phrase)
		{
			if (this.NPC.AI != null)
				this.NPC.AI.Phrases.Add(phrase);
		}

		/// <summary>
		/// Sets id of the NPC.
		/// </summary>
		/// <remarks>
		/// Only required for NPCs like Nao and Tin, avoid if possible!
		/// </remarks>
		/// <param name="entityId"></param>
		protected void SetId(long entityId)
		{
			this.NPC.EntityId = entityId;
		}

		/// <summary>
		/// Pulls down the hood of all equipped robes.
		/// </summary>
		public void SetHoodDown()
		{
			var item = this.NPC.Inventory.GetItemAt(Pocket.Robe, 0, 0);
			if (item != null)
				item.Info.State = 1;
			item = this.NPC.Inventory.GetItemAt(Pocket.RobeStyle, 0, 0);
			if (item != null)
				item.Info.State = 1;
		}

		/// <summary>
		/// Changes the NPC's AI.
		/// </summary>
		/// <param name="name"></param>
		public void SetAi(string name)
		{
			if (this.NPC.AI != null)
				this.NPC.AI.Dispose();

			var ai = ChannelServer.Instance.ScriptManager.GetAi(name, this.NPC);
			if (ai == null)
			{
				Log.Error("SetAi: AI '{0}' not found ({1})", name, this.GetType().Name);
				return;
			}

			this.NPC.AI = ai;
		}

		/// <summary>
		/// Adds a greeting to the NPC.
		/// </summary>
		/// <param name="memory">Memory needed for this message to appear.</param>
		/// <param name="greetingMessage">Message sent if the player's memory matches.</param>
		protected void AddGreeting(int memory, string greetingMessage)
		{
			if (!this.NPC.Greetings.ContainsKey(memory))
				this.NPC.Greetings.Add(memory, new List<string>());

			this.NPC.Greetings[memory].Add(greetingMessage);
		}

		// Functions
		// ------------------------------------------------------------------

		/// <summary>
		/// Sends Msg with Bgm element.
		/// </summary>
		/// <param name="fileName"></param>
		protected void SetBgm(string fileName)
		{
			this.Msg(new DialogBgm(fileName));
		}

		/// <summary>
		/// Opens shop for player.
		/// </summary>
		/// <param name="shopType"></param>
		protected void OpenShop(string shopType)
		{
			var shop = ChannelServer.Instance.ScriptManager.GetShop(shopType);
			if (shop == null)
			{
				Log.Unimplemented("Missing shop: {0}", shopType);
				this.Close("(Missing shop.)");
				return;
			}

			shop.OpenFor(this.Player, this.NPC);
		}

		/// <summary>
		/// Joins lines and sends them as Msg,
		/// but only once per creature and NPC per session.
		/// </summary>
		/// <param name="lines"></param>
		protected async Task Intro(params object[] lines)
		{
			if (this.Player.Vars.Temp["npc_intro:" + this.NPC.Name] == null)
			{
				// Explicit button and Select, so we don't get into the hooks
				// (that might do more than sending msgs) without clicking.
				this.Msg(Hide.Both, string.Join("<br/>", lines), this.Button("Continue"));
				await Select();
				this.Player.Vars.Temp["npc_intro:" + this.NPC.Name] = true;
			}

			await Hook("after_intro");
		}

		/// <summary>
		/// Adds item(s) to player's inventory.
		/// </summary>
		/// <param name="itemId"></param>
		/// <param name="amount"></param>
		/// <returns></returns>
		public bool GiveItem(int itemId, int amount = 1)
		{
			return this.Player.Inventory.Add(itemId, amount);
		}

		/// <summary>
		/// Adds an item to player's inventory with specific colors.
		/// </summary>
		/// <param name="itemId"></param>
		/// <param name="color1"></param>
		/// <param name="color2"></param>
		/// <param name="color3"></param>
		/// <returns></returns>
		public bool GiveItem(int itemId, uint color1, uint color2, uint color3)
		{
			var item = new Item(itemId);
			item.Info.Color1 = color1;
			item.Info.Color2 = color2;
			item.Info.Color3 = color3;

			return Player.Inventory.Add(item, true);
		}

		/// <summary>
		/// Removes item(s) from a player's inventory.
		/// </summary>
		/// <param name="itemId"></param>
		/// <param name="amount"></param>
		/// <returns></returns>
		public bool RemoveItem(int itemId, int amount = 1)
		{
			return this.Player.Inventory.Remove(itemId, amount);
		}

		/// <summary>
		/// Checks if player has item(s) in their inventory.
		/// </summary>
		/// <param name="itemId"></param>
		/// <param name="amount"></param>
		/// <returns></returns>
		public bool HasItem(int itemId, int amount = 1)
		{
			return this.Player.Inventory.Has(itemId, amount);
		}

		/// <summary>
		/// Execute Hook! Harhar.
		/// </summary>
		/// <remarks>
		/// Runs all hook funcs, one by one.
		/// </remarks>
		/// <param name="hookName"></param>
		/// <param name="args"></param>
		/// <returns></returns>
		protected async Task Hook(string hookName, params object[] args)
		{
			foreach (var hook in ChannelServer.Instance.ScriptManager.GetHooks(this.NPC.Name, hookName))
			{
				var result = await hook(this, args);
				switch (result)
				{
					case HookResult.Continue: continue; // Run next hook
					case HookResult.Break: return; // Stop and go back into the NPC
					case HookResult.End: this.Exit(); return; // Exit script
				}

			}
		}

		/// <summary>
		/// Returns true if quest is in progress.
		/// </summary>
		/// <param name="questId"></param>
		/// <param name="objective"></param>
		/// <returns></returns>
		public bool QuestActive(int questId, string objective = null)
		{
			return this.Player.Quests.IsActive(questId, objective);
		}

		/// <summary>
		/// Returns true if quest was completed.
		/// </summary>
		/// <param name="questId"></param>
		/// <returns></returns>
		public bool QuestCompleted(int questId)
		{
			return this.Player.Quests.IsComplete(questId);
		}

		/// <summary>
		/// Finishes objective in quest.
		/// </summary>
		/// <param name="questId"></param>
		/// <param name="objective"></param>
		/// <returns></returns>
		public bool FinishQuest(int questId, string objective)
		{
			return this.Player.Quests.Finish(questId, objective);
		}

		/// <summary>
		/// Returns current quest objective.
		/// </summary>
		/// <param name="questId"></param>
		/// <param name="objective"></param>
		/// <returns></returns>
		public string QuestObjective(int questId)
		{
			var quest = this.Player.Quests.Get(questId);
			if (quest == null)
				throw new Exception("NPC.GetQuestObjective: Player doesn't have quest '" + questId.ToString() + "'.");

			var current = quest.CurrentObjective;
			if (current == null)
				return null;

			return current.Ident;
		}

		/// <summary>
		/// (Re)Starts quest.
		/// </summary>
		/// <param name="questId"></param>
		public void StartQuest(int questId)
		{
			this.Player.Quests.Start(questId);
		}

		/// <summary>
		/// Completes quest (incl rewards).
		/// </summary>
		/// <param name="questId"></param>
		public void CompleteQuest(int questId)
		{
			this.Player.Quests.Complete(questId);
		}

		/// <summary>
		/// Displays notice.
		/// </summary>
		/// <param name="format"></param>
		/// <param name="args"></param>
		public void Notice(string format, params object[] args)
		{
			Send.Notice(this.Player, format, args);
		}

		/// <summary>
		/// Redeems code if found.
		/// </summary>
		/// <param name="code"></param>
		public bool RedeemCoupon(string code)
		{
			var script = ChannelServer.Instance.Database.GetCouponScript(code);
			if (script == null) return false;

			if (string.IsNullOrWhiteSpace(script))
			{
				Log.Error("CheckCouponCode: Empty script in '{0}'", code);
				return false;
			}

			var splitted = script.Split(':');
			if (splitted.Length < 2)
			{
				Log.Error("CheckCouponCode: Invalid script '{0}' in '{1}'", script, code);
				return false;
			}

			switch (splitted[0])
			{
				case "item":
					int itemId;
					if (!int.TryParse(splitted[1], out itemId))
						return false;

					var item = new Item(itemId);
					this.Player.Inventory.Add(item, true);
					Send.AcquireItemInfo(this.Player, item.EntityId);

					break;

				case "title":
					ushort titleId;
					if (!ushort.TryParse(splitted[1], out titleId))
						return false;
					this.Player.Titles.Enable(titleId);
					break;

				case "card":
					int cardId;
					if (!int.TryParse(splitted[1], out cardId))
						return false;
					ChannelServer.Instance.Database.AddCard(this.Player.Client.Account.Id, cardId, 0);
					break;

				case "petcard":
					int raceId;
					if (!int.TryParse(splitted[1], out raceId))
						return false;
					ChannelServer.Instance.Database.AddCard(this.Player.Client.Account.Id, MabiId.PetCardType, raceId);
					break;

				default:
					Log.Error("CheckCouponCode: Unknown script type '{0}' in '{1}'", splitted[0], code);
					return false;
			}

			ChannelServer.Instance.Database.UseCoupon(code);

			return true;
		}

		/// <summary>
		/// Opens bank window.
		/// </summary>
		public void OpenBank()
		{
			Send.OpenBank(this.Player, this.Player.Client.Account.Bank, BankTabRace.Human);
		}

		/// <summary>
		/// Returns true if player has the keyword.
		/// </summary>
		/// <param name="keyword"></param>
		public bool HasKeyword(string keyword)
		{
			return this.Player.Keywords.Has(keyword);
		}

		/// <summary>
		/// Returns true if player has the keyword.
		/// </summary>
		/// <param name="keyword"></param>
		public void GiveKeyword(string keyword)
		{
			if (!this.HasKeyword(keyword))
				this.Player.Keywords.Give(keyword);
		}

		/// <summary>
		/// Returns true if player has the keyword.
		/// </summary>
		/// <param name="keyword"></param>
		public void RemoveKeyword(string keyword)
		{
			if (this.HasKeyword(keyword))
				this.Player.Keywords.Remove(keyword);
		}

		/// <summary>
		/// Tries to repair item specified in the repair reply.
		/// </summary>
		/// <param name="repairReply"></param>
		/// <param name="rate"></param>
		/// <param name="tags"></param>
		/// <returns></returns>
		public RepairResult Repair(string repairReply, int rate, params string[] tags)
		{
			var result = new RepairResult();

			// Get item id: @repair(_all):123456789
			int pos = -1;
			if ((pos = repairReply.IndexOf(':')) == -1 || !long.TryParse(repairReply.Substring(pos + 1), out result.ItemEntityId))
			{
				Log.Warning("NpcScript.Repair: Player '{0}' (Account: {1}) sent invalid repair reply.", this.Player.EntityIdHex, this.Player.Client.Account.Id);
				return result;
			}

			// Perfect repair?
			var all = repairReply.StartsWith("@repair_all");

			// Get item
			result.Item = this.Player.Inventory.GetItem(result.ItemEntityId);
			if (result.Item == null || !tags.Any(a => result.Item.Data.HasTag(a)))
			{
				Log.Warning("NpcScript.Repair: Player '{0}' (Account: {1}) tried to repair invalid item.", this.Player.EntityIdHex, this.Player.Client.Account.Id);
				return result;
			}

			// Calculate points to repair
			result.Points = (!all ? 1000 : result.Item.OptionInfo.DurabilityMax - result.Item.OptionInfo.Durability);
			result.Points = (int)Math.Floor(result.Points / 1000f);

			// Check gold
			var cost = result.Item.GetRepairCost(rate, 1);
			if (this.Gold < cost * result.Points)
			{
				result.HadGold = false;
				return result;
			}

			// Take gold
			result.HadGold = true;
			this.Gold -= cost;

			// TODO: Luck?

			// TODO: Holy Water

			// Repair x times
			for (int i = 0; i < result.Points; ++i)
			{
				// Success
				if (this.Random(100) < rate)
				{
					result.Item.Durability += 1000;
					result.Successes++;
				}
				// Fail
				else
				{
					result.Item.OptionInfo.DurabilityMax = Math.Max(1000, result.Item.OptionInfo.DurabilityMax - 1000);
					if (result.Item.OptionInfo.DurabilityMax < result.Item.OptionInfo.Durability)
						result.Item.Durability -= 1000;
					result.Fails++;
				}
			}

			// Reduce gold, but only for successes
			this.Gold -= cost * result.Successes;

			// Update max dura
			if (result.Fails != 0)
				Send.ItemMaxDurabilityUpdate(this.Player, result.Item);

			// Update  dura
			if (result.Successes != 0)
				Send.ItemDurabilityUpdate(this.Player, result.Item);

			// Send result
			Send.ItemRepairResult(this.Player, result.Item, result.Successes);

			return result;
		}

		// Dialog
		// ------------------------------------------------------------------

		/// <summary>
		/// Sends one of the passed messages.
		/// </summary>
		/// <param name="msgs"></param>
		public void RndMsg(params string[] msgs)
		{
			var msg = this.RndStr(msgs);
			if (msg != null)
				this.Msg(msgs[Random(msgs.Length)]);
		}

		/// <summary>
		/// Sends one of the passed messages + FavorExpression.
		/// </summary>
		/// <param name="msgs"></param>
		public void RndFavorMsg(params string[] msgs)
		{
			var msg = this.RndStr(msgs);
			if (msg != null)
				this.Msg(Hide.None, msgs[Random(msgs.Length)], FavorExpression());
		}

		/// <summary>
		/// Sends dialog to player's client.
		/// </summary>
		/// <param name="elements"></param>
		public void Msg(params DialogElement[] elements)
		{
			this.Msg(Hide.None, elements);
		}

		/// <summary>
		/// Sends dialog to player's client.
		/// </summary>
		/// <param name="hide"></param>
		/// <param name="elements"></param>
		public void Msg(Hide hide, params DialogElement[] elements)
		{
			var element = new DialogElement();

			if (hide == Hide.Face || hide == Hide.Both)
				element.Add(new DialogPortrait(null));
			else if (this.NPC.DialogPortrait != null)
				element.Add(new DialogPortrait(this.NPC.DialogPortrait));

			if (hide == Hide.Name || hide == Hide.Both)
				element.Add(new DialogTitle(null));

			element.Add(elements);

			var xml = string.Format(
				"<call convention='thiscall' syncmode='non-sync'>" +
					"<this type='character'>{0}</this>" +
					"<function>" +
						"<prototype>void character::ShowTalkMessage(character, string)</prototype>" +
							"<arguments>" +
								"<argument type='character'>{0}</argument>" +
								"<argument type='string'>{1}</argument>" +
							"</arguments>" +
						"</function>" +
				"</call>",
			this.Player.EntityId, HttpUtility.HtmlEncode(element.ToString()));

			Send.NpcTalk(this.Player, xml);
		}

		/// <summary>
		/// Closes dialog box, by sending NpcTalkEndR, and leaves the NPC.
		/// </summary>
		/// <param name="message">Dialog closes immediately if null.</param>
		public void Close(string message = null)
		{
			this.Close(Hide.Both, message);
		}

		/// <summary>
		/// Closes dialog box, by sending NpcTalkEndR, and leaves the NPC.
		/// </summary>
		/// <param name="hide"></param>
		/// <param name="message">Dialog closes immediately if null.</param>
		public void Close(Hide hide, string message)
		{
			this.Close2(hide, message);
			this.Exit();
		}

		/// <summary>
		/// Sends NpcTalkEndR but doesn't leave NPC.
		/// </summary>
		/// <param name="message">Dialog closes immediately if null.</param>
		public void Close2(string message = null)
		{
			this.Close2(Hide.Both, message);
		}

		/// <summary>
		/// Sends NpcTalkEndR but doesn't leave NPC.
		/// </summary>
		/// <param name="hide"></param>
		/// <param name="message">Dialog closes immediately if null.</param>
		public void Close2(Hide hide, string message)
		{
			if (message != null)
			{
				if (hide == Hide.Face || hide == Hide.Both)
					message = new DialogPortrait(null).ToString() + message;
				else if (this.NPC.DialogPortrait != null)
					message = new DialogPortrait(this.NPC.DialogPortrait).ToString() + message;

				if (hide == Hide.Name || hide == Hide.Both)
					message = new DialogTitle(null).ToString() + message;
			}

			Send.NpcTalkEndR(this.Player, this.NPC.EntityId, message);
		}

		/// <summary>
		/// Throws exception to leave NPC.
		/// </summary>
		public void Exit()
		{
			throw new OperationCanceledException("NPC closed by script");
		}

		/// <summary>
		/// Informs the client that something can be selected now.
		/// </summary>
		public async Task<string> Select()
		{
			var script = string.Format(
				"<call convention='thiscall' syncmode='sync' session='{1}'>" +
					"<this type='character'>{0}</this>" +
					"<function>" +
						"<prototype>string character::SelectInTalk(string)</prototype>" +
						"<arguments><argument type='string'>&#60;keyword&#62;&#60;gift&#62;</argument></arguments>" +
					"</function>" +
				"</call>"
			, this.Player.EntityId, this.Player.Client.NpcSession.Id);

			Send.NpcTalk(this.Player, script);

			this.ConversationState = ConversationState.Select;
			await _resumeSignal.WaitAsync(_cancellation.Token);
			this.ConversationState = ConversationState.Ongoing;
			return _response;
		}

		/// <summary>
		/// Opens keyword window.
		/// </summary>
		/// <remarks>
		/// Select should be sent afterwards...
		/// so you can actually select a keyword.
		/// </remarks>
		protected void ShowKeywords()
		{
			var script = string.Format(
				"<call convention='thiscall' syncmode='non-sync'>" +
					"<this type='character'>{0}</this>" +
					"<function>" +
						"<prototype>void character::OpenTravelerMemo(string)</prototype>" +
						"<arguments>" +
							"<argument type='string'>(null)</argument>" +
						"</arguments>" +
					"</function>" +
				"</call>"
			, this.Player.EntityId);

			Send.NpcTalk(this.Player, script);
		}

		// Dialog factory
		// ------------------------------------------------------------------

		public DialogButton Button(string text, string keyword = null, string onFrame = null) { return new DialogButton(text, keyword, onFrame); }

		public DialogBgm Bgm(string file) { return new DialogBgm(file); }

		public DialogImage Image(string name) { return new DialogImage(name, false, 0, 0); }
		public DialogImage Image(string name, int width = 0, int height = 0) { return new DialogImage(name, false, width, height); }
		public DialogImage Image(string name, bool localize = false, int width = 0, int height = 0) { return new DialogImage(name, localize, width, height); }

		public DialogList List(string text, int height, string cancelKeyword, params DialogButton[] elements) { return new DialogList(text, height, cancelKeyword, elements); }
		public DialogList List(string text, params DialogButton[] elements) { return this.List(text, (int)elements.Length, elements); }
		public DialogList List(string text, int height, params DialogButton[] elements) { return this.List(text, height, "@end", elements); }

		public DialogInput Input(string title = "Input", string text = "", byte maxLength = 20, bool cancelable = true) { return new DialogInput(title, text, maxLength, cancelable); }

		public DialogAutoContinue AutoContinue(int duration) { return new DialogAutoContinue(duration); }

		public DialogFaceExpression Expression(string expression) { return new DialogFaceExpression(expression); }

		public DialogFaceExpression FavorExpression()
		{
			var favor = this.Favor;

			if (favor > 40)
				return Expression("love");
			if (favor > 15)
				return Expression("good");
			if (favor > -15)
				return Expression("normal");
			if (favor > -40)
				return Expression("bad");

			return Expression("hate");
		}

		public DialogMovie Movie(string file, int width, int height, bool loop = true) { return new DialogMovie(file, width, height, loop); }

		public DialogText Text(string format, params object[] args) { return new DialogText(format, args); }

		public DialogHotkey Hotkey(string text) { return new DialogHotkey(text); }

		public DialogMinimap Minimap(bool zoom, bool maxSize, bool center) { return new DialogMinimap(zoom, maxSize, center); }

		public DialogShowPosition ShowPosition(int region, int x, int y, int remainingTime) { return new DialogShowPosition(region, x, y, remainingTime); }

		public DialogShowDirection ShowDirection(int x, int y, int angle) { return new DialogShowDirection(x, y, angle); }

		public DialogSetDefaultName SetDefaultName(string name) { return new DialogSetDefaultName(name); }

		// ------------------------------------------------------------------

		protected enum ItemState : byte { Up = 0, Down = 1 }
		protected enum GiftReaction { Dislike, Neutral, Like, Love }
	}

	public enum Hide { None, Face, Name, Both }
	public enum ConversationState { Ongoing, Select, Ended }
	public enum HookResult { Continue, Break, End }

	public enum NpcMood
	{
		VeryStressed,
		Stressed,
		BestFriends,
		Friends,
		Hates,
		ReallyDislikes,
		Dislikes,
		Neutral,
		Likes,
		ReallyLikes,
		Love,
	}

	public struct RepairResult
	{
		public bool HadGold;
		public long ItemEntityId;
		public Item Item;
		public int Points;
		public int Successes;
		public int Fails;
	}

#if __MonoCS__
	// Added in Mono 3.0.8, adding it here for convenience.
	public static class SemaphoreSlimExtension
	{
		public static Task WaitAsync(this SemaphoreSlim slim, CancellationToken cancellationToken)
		{
			return Task.Factory.StartNew(() => slim.Wait(cancellationToken), cancellationToken);
		}
	}
#endif
}
