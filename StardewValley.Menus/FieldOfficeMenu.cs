using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley.Locations;
using StardewValley.TokenizableStrings;

namespace StardewValley.Menus
{
	public class FieldOfficeMenu : MenuWithInventory
	{
		private Texture2D fieldOfficeMenuTexture;

		private IslandFieldOffice office;

		private bool madeADonation;

		private bool gotReward;

		public List<ClickableComponent> pieceHolders = new List<ClickableComponent>();

		private float bearTimer;

		private float snakeTimer;

		private float batTimer;

		private float frogTimer;

		public FieldOfficeMenu(IslandFieldOffice office)
			: base(highlightBones, true, true, 16, 132)
		{
			FieldOfficeMenu fieldOfficeMenu = this;
			this.office = office;
			fieldOfficeMenuTexture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\FieldOfficeDonationMenu");
			Point topLeft = new Point(xPositionOnScreen + 32, yPositionOnScreen + 96);
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 76, topLeft.Y + 180, 64, 64), office.piecesDonated[0] ? ItemRegistry.Create("(O)823") : null)
			{
				label = "823"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 144, topLeft.Y + 180, 64, 64), office.piecesDonated[1] ? ItemRegistry.Create("(O)824") : null)
			{
				label = "824"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 212, topLeft.Y + 180, 64, 64), office.piecesDonated[2] ? ItemRegistry.Create("(O)823") : null)
			{
				label = "823"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 76, topLeft.Y + 112, 64, 64), office.piecesDonated[3] ? ItemRegistry.Create("(O)822") : null)
			{
				label = "822"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 144, topLeft.Y + 112, 64, 64), office.piecesDonated[4] ? ItemRegistry.Create("(O)821") : null)
			{
				label = "821"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 212, topLeft.Y + 112, 64, 64), office.piecesDonated[5] ? ItemRegistry.Create("(O)820") : null)
			{
				label = "820"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 412, topLeft.Y + 48, 64, 64), office.piecesDonated[6] ? ItemRegistry.Create("(O)826") : null)
			{
				label = "826"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 412, topLeft.Y + 128, 64, 64), office.piecesDonated[7] ? ItemRegistry.Create("(O)826") : null)
			{
				label = "826"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 412, topLeft.Y + 208, 64, 64), office.piecesDonated[8] ? ItemRegistry.Create("(O)825") : null)
			{
				label = "825"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 616, topLeft.Y + 36, 64, 64), office.piecesDonated[9] ? ItemRegistry.Create("(O)827") : null)
			{
				label = "827"
			});
			pieceHolders.Add(new ClickableComponent(new Rectangle(topLeft.X + 624, topLeft.Y + 156, 64, 64), office.piecesDonated[10] ? ItemRegistry.Create("(O)828") : null)
			{
				label = "828"
			});
			if (Game1.activeClickableMenu == null)
			{
				Game1.playSound("bigSelect");
			}
			for (int i = 0; i < pieceHolders.Count; i++)
			{
				ClickableComponent clickableComponent = pieceHolders[i];
				clickableComponent.upNeighborID = (clickableComponent.downNeighborID = (clickableComponent.rightNeighborID = (clickableComponent.leftNeighborID = -99998)));
				clickableComponent.myID = 1000 + i;
			}
			foreach (ClickableComponent item in inventory.GetBorder(InventoryMenu.BorderSide.Top))
			{
				item.upNeighborID = -99998;
			}
			foreach (ClickableComponent item2 in inventory.GetBorder(InventoryMenu.BorderSide.Right))
			{
				item2.rightNeighborID = 4857;
				item2.rightNeighborImmutable = true;
			}
			populateClickableComponentList();
			if (Game1.options.SnappyMenus)
			{
				snapToDefaultClickableComponent();
			}
			trashCan.leftNeighborID = (okButton.leftNeighborID = 11);
			exitFunction = delegate
			{
				if (fieldOfficeMenu.madeADonation)
				{
					string text = "Strings\\Locations:FieldOfficeDonated_" + Game1.random.Next(4);
					string text2 = Game1.content.LoadString(text);
					if (fieldOfficeMenu.gotReward)
					{
						text2 = text2 + "#$b#" + Game1.content.LoadString("Strings\\Locations:FieldOfficeDonated_Reward");
					}
					Game1.DrawDialogue(new Dialogue(office.getSafariGuy(), text, text2));
					if (fieldOfficeMenu.gotReward)
					{
						Game1.multiplayer.globalChatInfoMessage("FieldOfficeCompleteSet", Game1.player.Name);
					}
				}
			};
		}

		public override bool IsAutomaticSnapValid(int direction, ClickableComponent a, ClickableComponent b)
		{
			if (b.myID == 5948 && b.myID != 4857)
			{
				return false;
			}
			return base.IsAutomaticSnapValid(direction, a, b);
		}

		public override void snapToDefaultClickableComponent()
		{
			currentlySnappedComponent = getComponentWithID(0);
			snapCursorToCurrentSnappedComponent();
		}

		public static bool highlightBones(Item i)
		{
			if (i != null)
			{
				IslandFieldOffice office = Game1.RequireLocation<IslandFieldOffice>("IslandFieldOffice");
				switch (i.QualifiedItemId)
				{
				case "(O)820":
					if (!office.piecesDonated[5])
					{
						return true;
					}
					break;
				case "(O)821":
					if (!office.piecesDonated[4])
					{
						return true;
					}
					break;
				case "(O)822":
					if (!office.piecesDonated[3])
					{
						return true;
					}
					break;
				case "(O)823":
					if (!office.piecesDonated[0] || !office.piecesDonated[2])
					{
						return true;
					}
					break;
				case "(O)824":
					if (!office.piecesDonated[1])
					{
						return true;
					}
					break;
				case "(O)825":
					if (!office.piecesDonated[8])
					{
						return true;
					}
					break;
				case "(O)826":
					if (!office.piecesDonated[7] || !office.piecesDonated[6])
					{
						return true;
					}
					break;
				case "(O)827":
					if (!office.piecesDonated[9])
					{
						return true;
					}
					break;
				case "(O)828":
					if (!office.piecesDonated[10])
					{
						return true;
					}
					break;
				}
			}
			return false;
		}

		public static int getPieceIndexForDonationItem(string qualifiedItemId)
		{
			switch (qualifiedItemId)
			{
			case "(O)820":
				return 5;
			case "(O)821":
				return 4;
			case "(O)822":
				return 3;
			case "(O)823":
				return 0;
			case "(O)824":
				return 1;
			case "(O)825":
				return 8;
			case "(O)826":
				return 7;
			case "(O)827":
				return 9;
			case "(O)828":
				return 10;
			default:
				return -1;
			}
		}

		public static int getDonationPieceIndexNeededForSpot(int donationSpotIndex)
		{
			switch (donationSpotIndex)
			{
			case 5:
				return 820;
			case 4:
				return 821;
			case 3:
				return 822;
			case 0:
			case 2:
				return 823;
			case 1:
				return 824;
			case 8:
				return 825;
			case 6:
			case 7:
				return 826;
			case 9:
				return 827;
			case 10:
				return 828;
			default:
				return -1;
			}
		}

		public override void receiveLeftClick(int x, int y, bool playSound = true)
		{
			base.receiveLeftClick(x, y, playSound);
			if (base.heldItem == null)
			{
				return;
			}
			int index = getPieceIndexForDonationItem(base.heldItem.QualifiedItemId);
			if (index == -1)
			{
				return;
			}
			string qualifiedItemId = base.heldItem.QualifiedItemId;
			if (!(qualifiedItemId == "(O)823"))
			{
				if (qualifiedItemId == "(O)826")
				{
					if (!donate(7, x, y))
					{
						donate(6, x, y);
					}
				}
				else
				{
					donate(index, x, y);
				}
			}
			else if (!donate(0, x, y))
			{
				donate(2, x, y);
			}
		}

		protected override void cleanupBeforeExit()
		{
			base.cleanupBeforeExit();
			if (office != null && office.isRangeAllTrue(0, 11) && office.plantsRestoredRight.Value && office.plantsRestoredLeft.Value && !Game1.player.hasOrWillReceiveMail("fieldOfficeFinale"))
			{
				office.triggerFinaleCutscene();
			}
		}

		private bool donate(int index, int x, int y)
		{
			if (pieceHolders[index].containsPoint(x, y) && pieceHolders[index].item == null)
			{
				string qualifiedItemId = base.heldItem.QualifiedItemId;
				pieceHolders[index].item = ItemRegistry.Create(qualifiedItemId);
				base.heldItem.Stack--;
				if (base.heldItem.Stack <= 0)
				{
					base.heldItem = null;
				}
				Game1.playSound("newArtifact");
				checkForSetFinish();
				gotReward = office.donatePiece(index);
				Game1.multiplayer.globalChatInfoMessage("FieldOfficeDonation", Game1.player.Name, TokenStringBuilder.ItemName(qualifiedItemId));
				madeADonation = true;
				return true;
			}
			return false;
		}

		public void checkForSetFinish()
		{
			if (!office.centerSkeletonRestored.Value && pieceHolders[0].item != null && pieceHolders[1].item != null && pieceHolders[2].item != null && pieceHolders[3].item != null && pieceHolders[4].item != null && pieceHolders[5].item != null)
			{
				DelayedAction.functionAfterDelay(delegate
				{
					bearTimer = 500f;
					Game1.playSound("camel");
				}, 700);
			}
			if (!office.snakeRestored.Value && pieceHolders[6].item != null && pieceHolders[7].item != null && pieceHolders[8].item != null)
			{
				DelayedAction.functionAfterDelay(delegate
				{
					snakeTimer = 1500f;
					Game1.playSound("steam");
				}, 700);
			}
			if (!office.batRestored.Value && pieceHolders[9].item != null)
			{
				DelayedAction.functionAfterDelay(delegate
				{
					batTimer = 1500f;
					Game1.playSound("batScreech");
				}, 700);
			}
			if (!office.frogRestored.Value && pieceHolders[10].item != null)
			{
				DelayedAction.functionAfterDelay(delegate
				{
					frogTimer = 1000f;
					Game1.playSound("croak");
				}, 700);
			}
		}

		public override void update(GameTime time)
		{
			base.update(time);
			if (bearTimer > 0f)
			{
				bearTimer -= (float)time.ElapsedGameTime.TotalMilliseconds;
			}
			if (snakeTimer > 0f)
			{
				snakeTimer -= (float)time.ElapsedGameTime.TotalMilliseconds;
			}
			if (batTimer > 0f)
			{
				batTimer -= (float)time.ElapsedGameTime.TotalMilliseconds;
			}
			if (frogTimer > 0f)
			{
				frogTimer -= (float)time.ElapsedGameTime.TotalMilliseconds;
			}
		}

		public override void draw(SpriteBatch b)
		{
			base.draw(b, true, false, 0, 80, 80);
			b.Draw(fieldOfficeMenuTexture, new Vector2(xPositionOnScreen + 32, yPositionOnScreen + 96), new Rectangle(0, 0, 204, 80), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.1f);
			b.Draw(fieldOfficeMenuTexture, new Vector2(xPositionOnScreen + width - 160, (float)(yPositionOnScreen + 108) + ((batTimer > 0f) ? ((float)Math.Sin((1500f - batTimer) / 80f) * 64f / 4f) : 0f)), new Rectangle(68, 84, 30, 20), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.1f);
			foreach (ClickableComponent c in pieceHolders)
			{
				c.item?.drawInMenu(b, Utility.PointToVector2(c.bounds.Location), 1f);
			}
			if (bearTimer > 0f)
			{
				b.Draw(fieldOfficeMenuTexture, new Vector2(xPositionOnScreen + 32 + 240, yPositionOnScreen + 96 + 36), new Rectangle(0, 81, 37, 29), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.1f);
			}
			else if (snakeTimer > 0f && snakeTimer / 300f % 2f != 0f)
			{
				b.Draw(fieldOfficeMenuTexture, new Vector2(xPositionOnScreen + 32 + 484, yPositionOnScreen + 96 + 232), new Rectangle(47, 84, 19, 19), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.1f);
			}
			else if (frogTimer > 0f)
			{
				b.Draw(fieldOfficeMenuTexture, new Vector2(xPositionOnScreen + 32 + 708, yPositionOnScreen + 96 + 140), new Rectangle(100, 89, 18, 7), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.1f);
			}
			if (base.heldItem != null)
			{
				int highlight = getPieceIndexForDonationItem(base.heldItem.QualifiedItemId);
				if (highlight != -1)
				{
					drawHighlightedSquare(highlight, b);
				}
			}
			drawMouse(b);
			base.heldItem?.drawInMenu(b, new Vector2(Game1.getOldMouseX() + 16, Game1.getOldMouseY() + 16), 1f);
		}

		private void drawHighlightedSquare(int index, SpriteBatch b)
		{
			Rectangle source = default(Rectangle);
			switch (base.heldItem.QualifiedItemId)
			{
			case "(O)820":
			case "(O)821":
			case "(O)822":
			case "(O)823":
			case "(O)824":
				source = new Rectangle(119, 86, 18, 18);
				break;
			case "(O)825":
			case "(O)826":
				source = new Rectangle(138, 86, 18, 18);
				break;
			case "(O)827":
				source = new Rectangle(157, 86, 18, 18);
				break;
			case "(O)828":
				source = new Rectangle(176, 86, 18, 18);
				break;
			}
			if (pieceHolders[index].item == null)
			{
				b.Draw(fieldOfficeMenuTexture, Utility.PointToVector2(pieceHolders[index].bounds.Location) + new Vector2(-1f, -1f) * 4f, source, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.1f);
			}
			string qualifiedItemId = base.heldItem.QualifiedItemId;
			if (!(qualifiedItemId == "(O)823"))
			{
				if (qualifiedItemId == "(O)826" && index == 7 && pieceHolders[6].item == null)
				{
					b.Draw(fieldOfficeMenuTexture, Utility.PointToVector2(pieceHolders[6].bounds.Location) + new Vector2(-1f, -1f) * 4f, source, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.1f);
				}
			}
			else if (index == 0 && pieceHolders[2].item == null)
			{
				b.Draw(fieldOfficeMenuTexture, Utility.PointToVector2(pieceHolders[2].bounds.Location) + new Vector2(-1f, -1f) * 4f, source, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.1f);
			}
		}
	}
}
