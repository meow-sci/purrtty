using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Reflection;
using Brutal.Numerics;

namespace StarMap.SimpleExampleMod
{
    public class QuickViewManager
    {
        private List<QuickView> _views = new List<QuickView>();
        private string _filePath;

        public QuickViewManager()
        {
            // Determine path for saving
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string directory = Path.GetDirectoryName( assemblyLocation ) ?? ".";
            _filePath = Path.Combine( directory, "quickviews.json" );
            
            LoadViews();
        }

        public void SaveView( string name, double3 offset, float yaw, float pitch, float roll, float fov )
        {
            _views.Add( new QuickView( name, offset, yaw, pitch, roll, fov ) );
            Persist();
        }

        public void DeleteView( int index )
        {
            if ( index >= 0 && index < _views.Count )
            {
                _views.RemoveAt( index );
                Persist();
            }
        }

        public List<QuickView> GetViews()
        {
            return _views;
        }

        private void Persist()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize( _views, options );
                File.WriteAllText( _filePath, jsonString );
            }
            catch ( Exception ex )
            {
                Console.WriteLine( $"QuickViewManager - Error saving views: {ex.Message}" );
            }
        }

        private void LoadViews()
        {
            if ( !File.Exists( _filePath ) ) return;

            try
            {
                string jsonString = File.ReadAllText( _filePath );
                var loaded = JsonSerializer.Deserialize<List<QuickView>>( jsonString );
                if ( loaded != null )
                {
                    _views = loaded;
                }
            }
            catch ( Exception ex )
            {
                Console.WriteLine( $"QuickViewManager - Error loading views: {ex.Message}" );
            }
        }
    }
}

