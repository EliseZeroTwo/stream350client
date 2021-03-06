using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using K4os.Compression.LZ4.Streams;
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
        public uint FrameSize;
        public ushort x;
        public ushort y;
    }
    
    public class RG350
    {
        private static readonly Dictionary<byte, uint> PixelFormatMap = new Dictionary<byte, uint>
        {
            {15, SDL.SDL_PIXELFORMAT_RGB555},
            {16, SDL.SDL_PIXELFORMAT_RGB565},
            {32, SDL.SDL_PIXELFORMAT_RGBX8888}
        };

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
            Log.Msg($"Metadata Time! Prev metadata: {Meta.BitsPerPixel}, {Meta.FrameSize} {Meta.x}, {Meta.y}");
            Metadata newMeta;
            newMeta.BitsPerPixel = reader.ReadByte();
            newMeta.FrameSize = reader.ReadUInt32();
            newMeta.x = reader.ReadUInt16();
            newMeta.y = reader.ReadUInt16();
            Log.Msg($"New metadata: {newMeta.BitsPerPixel}, {newMeta.FrameSize} {newMeta.x}, {newMeta.y}");
            if (newMeta.BitsPerPixel != Meta.BitsPerPixel || newMeta.x != Meta.x || newMeta.y != Meta.y)
            {
                Meta = newMeta;
                ReintializeWr();
            }
            else if (newMeta.FrameSize != Meta.FrameSize)
                Meta = newMeta;
        }

        public byte[] GetFrame()
        {
            UpdateMetadata();

            var screenSize = (Meta.x * Meta.y * Meta.BitsPerPixel) / 8;

            Log.Msg($"Meta: {Meta.BitsPerPixel}. {Meta.x}, {Meta.y}. SS: {screenSize}, comp: {Meta.FrameSize}");
            var compBuffer = new byte[Meta.FrameSize];

            
            //var readLen = client.Client.Receive(buffer);
            int offset = 0;
            while (offset < Meta.FrameSize)
            {
                int read = stream.Read(compBuffer, offset, (int)Meta.FrameSize - offset);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            Stream compStream = new MemoryStream(compBuffer);

            Stream outStream = LZ4Stream.Decode(compStream);
            var decompBuffer = new byte[screenSize];
            outStream.Read(decompBuffer);
            return decompBuffer;
        }

        private void ReintializeWr()
        {
            if (Renderer != IntPtr.Zero)
                SDL.SDL_DestroyRenderer(Renderer);
            
            if (Window != IntPtr.Zero)
                SDL.SDL_DestroyWindow(Window);
            
            if (tex_mem != IntPtr.Zero)
                SDL.SDL_free(tex_mem);

            Console.WriteLine($"Meta: {Meta.BitsPerPixel}, {Meta.FrameSize}, {Meta.x}, {Meta.y}");

            Window = SDL.SDL_CreateWindow($"stream350client <{IP}>", SDL.SDL_WINDOWPOS_CENTERED, SDL.SDL_WINDOWPOS_CENTERED, Meta.x, Meta.y, 0);
            if (Window == IntPtr.Zero)
                throw new Exception("Failed to initialize window");
            
            Renderer = SDL.SDL_CreateRenderer(Window, -1, 0);
            if (Renderer == IntPtr.Zero)
                throw new Exception("Failed to initialize renderer");
            

            texture = SDL.SDL_CreateTexture(Renderer, PixelFormatMap[Meta.BitsPerPixel],
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
                var key_down_to_send = new List<ushort>();
                var key_up_to_send = new List<ushort>();
                while (SDL.SDL_PollEvent(out SDL.SDL_Event e) != 0)
                {
                    switch (e.type)
                    {
                        case SDL.SDL_EventType.SDL_QUIT:
                            Environment.Exit(0);
                            break;
                        case SDL.SDL_EventType.SDL_KEYDOWN:
                            if (e.key.repeat != 0)
                                break;
                            switch (e.key.keysym.sym)
                            {
                                case SDL.SDL_Keycode.SDLK_w:
                                    key_down_to_send.Add((ushort) Keys.DPAD_UP);
                                    break;
                                case SDL.SDL_Keycode.SDLK_a:
                                    key_down_to_send.Add((ushort) Keys.DPAD_LEFT);
                                    break;
                                case SDL.SDL_Keycode.SDLK_s:
                                    key_down_to_send.Add((ushort) Keys.DPAD_DOWN);
                                    break;
                                case SDL.SDL_Keycode.SDLK_d:
                                    key_down_to_send.Add((ushort) Keys.DPAD_RIGHT);
                                    break;
                                case SDL.SDL_Keycode.SDLK_q:
                                    key_down_to_send.Add((ushort) Keys.BTN_B);
                                    break;
                                case SDL.SDL_Keycode.SDLK_e:
                                    key_down_to_send.Add((ushort) Keys.BTN_A);
                                    break;
                                case SDL.SDL_Keycode.SDLK_z:
                                    key_down_to_send.Add((ushort) Keys.BTN_X);
                                    break;
                                case SDL.SDL_Keycode.SDLK_x:
                                    key_down_to_send.Add((ushort) Keys.BTN_Y);
                                    break;
                            }
                            break;
                        case SDL.SDL_EventType.SDL_KEYUP:
                            switch (e.key.keysym.sym)
                            {
                                case SDL.SDL_Keycode.SDLK_w:
                                    key_up_to_send.Add((ushort) Keys.DPAD_UP);
                                    break;
                                case SDL.SDL_Keycode.SDLK_a:
                                    key_up_to_send.Add((ushort) Keys.DPAD_LEFT);
                                    break;
                                case SDL.SDL_Keycode.SDLK_s:
                                    key_up_to_send.Add((ushort) Keys.DPAD_DOWN);
                                    break;
                                case SDL.SDL_Keycode.SDLK_d:
                                    key_up_to_send.Add((ushort) Keys.DPAD_RIGHT);
                                    break;
                                case SDL.SDL_Keycode.SDLK_q:
                                    key_up_to_send.Add((ushort) Keys.BTN_B);
                                    break;
                                case SDL.SDL_Keycode.SDLK_e:
                                    key_up_to_send.Add((ushort) Keys.BTN_A);
                                    break;
                                case SDL.SDL_Keycode.SDLK_z:
                                    key_up_to_send.Add((ushort) Keys.BTN_X);
                                    break;
                                case SDL.SDL_Keycode.SDLK_x:
                                    key_up_to_send.Add((ushort) Keys.BTN_Y);
                                    break;
                            }
                            break;
                    }
                }
                stream.WriteByte((byte)key_down_to_send.Count);
                foreach (var key in key_down_to_send)
                {
                    Console.WriteLine("OwO");
                    stream.Write(BitConverter.GetBytes(key));
                }

                stream.WriteByte((byte)key_up_to_send.Count);
                Console.WriteLine((byte)key_up_to_send.Count);
                foreach (var key in key_up_to_send)
                {
                    Console.WriteLine("UwU");
                    stream.Write(BitConverter.GetBytes(key));   
                }

                key_down_to_send.Clear();
                key_up_to_send.Clear();
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