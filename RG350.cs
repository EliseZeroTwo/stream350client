using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using SDL2;

namespace stream350client
{
    public enum Colorspace : byte
    {
        RGB565 = 0,
        RGBx = 1
    }
    
    public struct Metadata
    {
        public byte BitsPerPixel;
        public ushort x;
        public ushort y;
    }
    
    public class RG350
    {

        private IntPtr Window = IntPtr.Zero;
        private IntPtr Renderer = IntPtr.Zero;
        private readonly string IP;
        private Metadata Meta = new Metadata();
        private IntPtr texture;
        private byte FrameCounter = 0;
        private IntPtr tex_mem = IntPtr.Zero;

        private TcpClient client;
        private readonly BinaryReader reader;
        private NetworkStream stream;

        private void UpdateMetadata()
        {
            Log.Msg($"Metadata Time! Prev metadata: {Meta.BitsPerPixel}. {Meta.x}, {Meta.y}");
            Metadata newMeta;
            newMeta.BitsPerPixel = reader.ReadByte();
            newMeta.x = reader.ReadUInt16();
            newMeta.y = reader.ReadUInt16();
            Log.Msg($"New metadata: {newMeta.BitsPerPixel}. {newMeta.x}, {newMeta.y}");
            if (newMeta.BitsPerPixel != Meta.BitsPerPixel || newMeta.x != Meta.x || newMeta.y != Meta.y)
            {
                Meta = newMeta;
                ReintializeWr();
            }
        }
        
        public byte[] GetFrame()
        {
            Log.Msg($"Frame {FrameCounter}");
            //if (++FrameCounter == 60)
                //UpdateMetadata();

            
            var screenSize = Meta.x * Meta.y * (Meta.BitsPerPixel / 8);
            
            Log.Msg($"Meta: {Meta.BitsPerPixel}. {Meta.x}, {Meta.y}. SS: {screenSize}");
            var buffer = new byte[screenSize];

            
            //var readLen = client.Client.Receive(buffer);
            int offset = 0;
            while (offset < screenSize)
            {
                int read = stream.Read(buffer, offset, screenSize - offset);
                if (read == 0)
                    throw new System.IO.EndOfStreamException();
                offset += read;
            }
            
            return buffer;
        }

        private void ReintializeWr()
        {
            if (Renderer != IntPtr.Zero)
                SDL.SDL_DestroyRenderer(Renderer);
            
            if (Window != IntPtr.Zero)
                SDL.SDL_DestroyWindow(Window);
            
            if (tex_mem != IntPtr.Zero)
                SDL.SDL_free(tex_mem);

            Console.WriteLine($"Meta: {Meta.BitsPerPixel}. {Meta.x}, {Meta.y}");

            Window = SDL.SDL_CreateWindow($"stream350client <{IP}>", SDL.SDL_WINDOWPOS_CENTERED, SDL.SDL_WINDOWPOS_CENTERED, Meta.x, Meta.y, 0);
            if (Window == IntPtr.Zero)
                throw new Exception("Failed to initialize window");
            
            Renderer = SDL.SDL_CreateRenderer(Window, -1, 0);
            if (Renderer == IntPtr.Zero)
                throw new Exception("Failed to initialize renderer");
            

            texture = SDL.SDL_CreateTexture(Renderer, Meta.BitsPerPixel == 16 ? SDL.SDL_PIXELFORMAT_RGB565 : SDL.SDL_PIXELFORMAT_RGBX8888,
                (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, Meta.x, Meta.y);

            tex_mem = SDL.SDL_malloc((IntPtr) (Meta.x * Meta.y * Meta.BitsPerPixel / 8));
        }

        public void DrawFrame()
        {
            SDL.SDL_RenderClear(Renderer);
            var nextFrame = GetFrame();
            
            Marshal.Copy(nextFrame, 0, tex_mem, Meta.x * Meta.y * Meta.BitsPerPixel / 8);
            SDL.SDL_UpdateTexture(texture, IntPtr.Zero, tex_mem, Meta.x * Meta.BitsPerPixel / 8);
            SDL.SDL_RenderCopy(Renderer, texture, IntPtr.Zero, IntPtr.Zero);
            SDL.SDL_SetRenderTarget(Renderer, IntPtr.Zero);
            SDL.SDL_RenderPresent(Renderer);
        }

        private uint nextTime = SDL.SDL_GetTicks() + 1000 / 60;
        private uint TimeLeft()
        {
            var now = SDL.SDL_GetTicks();
            var res = nextTime > now ? nextTime - now : 0;
            nextTime += 1000 / 60;
            return res;
        }

        public void ThreadMain()
        {
            while (true)
            {
                DrawFrame();
                while (SDL.SDL_PollEvent(out SDL.SDL_Event e) != 0)
                {
                    switch (e.type)
                    {
                        case SDL.SDL_EventType.SDL_QUIT:
                            Environment.Exit(0);
                            break;
                        case SDL.SDL_EventType.SDL_KEYDOWN:
                            if (e.key.keysym.sym == SDL.SDL_Keycode.SDLK_q)
                                Environment.Exit(0);
                            break;
                    }
                }
            }
        }
        
        public RG350(string ip, int port=420)
        {
            IP = ip;
            try
            {
                client = new TcpClient();
                client.Connect(ip, port);
            }
            catch
            {
                Console.WriteLine($"Failed to connect to console {ip}:{port}");
                throw;
            }

            client.Client.Blocking = true;
            stream = client.GetStream();
            reader = new BinaryReader(client.GetStream());
            UpdateMetadata();
        }
    }
}