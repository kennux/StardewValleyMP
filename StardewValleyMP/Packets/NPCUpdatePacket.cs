﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using StardewValley;
using StardewValley.Objects;
using StardewModdingAPI;
using Microsoft.Xna.Framework;
using Object = StardewValley.Object;
using StardewValleyMP.States;

namespace StardewValleyMP.Packets
{
    // Server <-> Client
    // An NPC changed in a way other players need to know now
    public class NPCUpdatePacket : Packet
    {
        public string name;
        public NPCState state = new NPCState();

        public NPCUpdatePacket() : base(ID.NPCUpdate)
        {
        }

        public NPCUpdatePacket(NPC npc)
            : this()
        {
            name = npc.name;
            state = new NPCState(npc);
            if (state.defaultMap == null) state.defaultMap = ""; // No idea why this happens
        }

        protected override void read(BinaryReader reader)
        {
            name = reader.ReadString();
            state.datingFarmer = reader.ReadBoolean();
            state.married = reader.ReadBoolean();
            state.defaultMap = reader.ReadString();
            if (state.defaultMap == "") state.defaultMap = null;
            state.defaultX = reader.ReadSingle();
            state.defaultY = reader.ReadSingle();

            if (state.defaultMap == null)
                Log.Async("! RECEIVED NPC UPDATE WITH NULL: " + name);
        }

        protected override void write(BinaryWriter writer)
        {
            if (state.defaultMap == "" || state.defaultMap == null)
                Log.Async("! SENDING NPC UPDATE WITH NULL: " + name);

            writer.Write(name);
            writer.Write(state.datingFarmer);
            writer.Write(state.married);
            writer.Write(state.defaultMap != null ? state.defaultMap : "");
            writer.Write(state.defaultX);
            writer.Write(state.defaultY);
        }

        public override void process(Client client)
        {
            state.defaultMap = Multiplayer.processLocationNameForPlayerUnique(null, state.defaultMap);

            if (name == Game1.player.spouse)
                return;
            
            process();
        }

        public override void process(Server server, Server.Client client)
        {
            state.defaultMap = Multiplayer.processLocationNameForPlayerUnique(client.farmer, state.defaultMap);

            if (name == Game1.player.spouse) return;
            foreach ( Server.Client other in server.clients )
            {
                if (other == client) continue;
                if (name == other.farmer.spouse)
                    return;
            }

            process();
            server.broadcast(this, client.id);
        }

        private void process()
        {
            NPCMonitor.updateNPC(name, state);
        }
    }
}
