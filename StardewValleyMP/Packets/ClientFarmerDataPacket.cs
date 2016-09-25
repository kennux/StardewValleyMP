﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Characters;
using StardewModdingAPI;
using StardewValleyMP.Vanilla;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace StardewValleyMP.Packets
{
    // Client -> Server
    // Send the server info about yourself.
    public class ClientFarmerDataPacket : Packet
    {
        public string xml;

        public ClientFarmerDataPacket() : base( ID.ClientFarmerData )
        {
        }

        public ClientFarmerDataPacket(string theXml)
            : this()
        {
            xml = theXml;
        }

        protected override void read(BinaryReader reader)
        {
            xml = reader.ReadString();
        }

        protected override void write(BinaryWriter writer)
        {
            writer.Write(xml);
        }

        public override void process( Server server, Server.Client client )
        {
            Log.Async("Got farmer data for client " + client.id);

            //Farmer old = client.farmer;
            SaveGame theirs = (SaveGame)SaveGame.serializer.Deserialize(Util.stringStream(xml));

            if (client.farmer == null)
            {
                ChatMenu.chat.Add(new ChatEntry(null, theirs.player.name + " has connected."));
                server.broadcast(new ChatPacket(255, theirs.player.name + " has connected."), client.id);

                String str = "Currently playing: ";
                str += NewLoadMenu.pendingSelected.name;
                foreach ( Server.Client other in server.clients )
                {
                    if (other == client || other.farmer == null) continue;
                    str += ", " + other.farmer.name;
                }
                client.send(new ChatPacket(255, str));
            }

            client.farmerXml = Util.serialize<Farmer>(theirs.player);
            client.farmer = theirs.player;
            client.farmer.uniqueMultiplayerID += 1 + client.id;

            NewSaveGame.loadDataToFarmer(client.farmer, client.farmer);

            //if(!server.playing)
            //if (server.playing) client.farmer = old;
            
            // About second-day-sleeping crashes:
            // So just adding the location directly puts the raw deserialized one into the game.
            // The raw deserialized one doesn't have the tiles and stuff loaded. Just the game data.
            // I think this is why vanilla copies data over in loadDataToLocations instead of using
            // the loaded objects directly. Why not just postpone loading until later, I don't know.
            //
            // So, when the second day begins, otherFarmer.currentLocation was still set to the
            // previous day's farm house[*]. On day two, the 'good' one[**] was removed, so when they go
            // back in, the bad one is used. Basically, I need to make them all the 'good' one.
            // For now, I'm just going to reload the needed data for this client's farmhouse.
            // I'll figure out how to do it 'properly' later. Maybe. (My mind is muddled today.)
            // 
            // [*] Looking at addFixedLocationToOurWorld now you'll see that this isn't the case.
            // I added the part about fixing Farmer.currentLocation as I was going through this 
            // thought process. So things will break more obviously if something like this happens
            // again.
            //
            // [**] The first day's farmhouse is okay because in loadDataToLocations, (called in
            // NewSaveGame.getLoadEnumerator), the map is reloaded from FarmHouse_setMapForUpgradeLevel. 
            // If CO-OP weren't on, worse things would happen, because things besides the farm house
            // would need loading (see Multiplayer.isPlayerUnique). The client doesn't have this
            // issue because they do the whole loading process each day anyways.
            //
            // Of course, the whole second-day-crash doesn't happen when I test it on localhost. Hence
            // why this was so annoying. And probably why I documented all this.
            foreach (GameLocation theirLoc in theirs.locations)
            {
                if ( theirLoc.name == "FarmHouse" )
                    NewSaveGame.FarmHouse_setMapForUpgradeLevel( theirLoc as FarmHouse );
            }

            fixPetDuplicates(theirs);

            foreach (string mail in Multiplayer.checkMail)
            {
                if (client.farmer.mailForTomorrow.Contains(mail))
                {
                    if (!SaveGame.loaded.player.mailForTomorrow.Contains(mail))
                        SaveGame.loaded.player.mailForTomorrow.Add(mail);
                    if (Game1.player != null && !Game1.player.mailForTomorrow.Contains(mail))
                        Game1.player.mailForTomorrow.Add(mail);
                }
                if (client.farmer.mailForTomorrow.Contains(mail + "%&NL&%"))
                {
                    if (!SaveGame.loaded.player.mailForTomorrow.Contains(mail + "%&NL&%"))
                        SaveGame.loaded.player.mailForTomorrow.Add(mail + "%&NL&%");
                    if (Game1.player != null && !Game1.player.mailForTomorrow.Contains(mail + "%&NL&%"))
                        Game1.player.mailForTomorrow.Add(mail + "%&NL&%");
                }
            }

            client.stage = Server.Client.NetStage.WaitingForStart;
        }

        private void addFixedLocationToOurWorld( GameLocation loc, string oldName )
        {
            Log.Async("Adding: " + oldName + " -> " + loc.name + " (" + loc + ")");
            if ( oldName != "FarmHouse" )
            {
                Log.Async("READ THE BLOCK OF COMMENTS IN THE ABOVE FUNCTION");
                return;
            }

            if ( Multiplayer.isPlayerUnique( oldName ) )
            {
                bool found = false;
                for ( int i = 0; i < Game1.locations.Count; ++i )
                {
                    if ( Game1.locations[ i ].name.ToLower().Equals( loc.name.ToLower() ) )
                    {
                        loc.farmers.AddRange(Game1.locations[i].farmers);
                        Game1.locations[i].farmers.Clear();
                        foreach (Farmer farmer in loc.farmers)
                        {
                            farmer.currentLocation = loc;
                        }
                        Game1.locations[i] = loc;
                        found = true;
                        break;
                    }
                }
                if (!found) Game1.locations.Add(loc);

                found = false;
                for (int i = 0; i < SaveGame.loaded.locations.Count; ++i)
                {
                    if (SaveGame.loaded.locations[i].name.ToLower().Equals( loc.name.ToLower() ) )
                    {
                        loc.farmers.AddRange(SaveGame.loaded.locations[i].farmers);
                        SaveGame.loaded.locations[i].farmers.Clear();
                        foreach ( Farmer farmer in loc.farmers )
                        {
                            farmer.currentLocation = loc;
                        }
                        SaveGame.loaded.locations[i] = loc;
                        found = true;
                        break;
                    }
                }
                if (!found) SaveGame.loaded.locations.Add(loc);
            }
        }
        
        private void fixPetDuplicates( SaveGame world )
        {
            // Remove all instances of their pets.
            // Since we're the host, we don't need to move our pets over to their farm like the client does.

            Farm theirFarm = null;
            FarmHouse theirHouse = null;
            for (int i = 0; i < world.locations.Count; ++i)
            {
                if (world.locations[i].name.Equals("Farm"))
                {
                    theirFarm = world.locations[i] as Farm;
                }
                else if (world.locations[i].name.Equals("FarmHouse"))
                {
                    theirHouse = world.locations[i] as FarmHouse;
                }
            }
            for (int i = 0; i < theirFarm.characters.Count; ++i)
            {
                NPC npc = theirFarm.characters[i];
                if (npc is Pet)
                {
                    theirFarm.characters.Remove(npc);
                    --i;
                    continue;
                }
            }
            for (int i = 0; i < theirHouse.characters.Count; ++i)
            {
                NPC npc = theirHouse.characters[i];
                if (npc is Pet)
                {
                    theirHouse.characters.Remove(npc);
                    --i;
                    continue;
                }
            }
        }
    }
}
