using System;
using SDL2;

namespace stream350client
{
    class Program
    {
        static void Main(string[] args)
        {
            Log.Init();
            if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) < 0)
                throw new Exception($"Failed to initialise SDL2");

            new RG350("10.1.1.2").ThreadMain();
        }
    }
}