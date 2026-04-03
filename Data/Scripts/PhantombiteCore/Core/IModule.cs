using System;
using Sandbox.ModAPI;

namespace PhantombiteCore.Core
{
    /// <summary>
    /// Base interface for all PhantombiteCore modules
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// Unique module identifier
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// Initialize module - called once on mod load
        /// </summary>
        void Init();

        /// <summary>
        /// Update module - called every frame
        /// </summary>
        void Update();

        /// <summary>
        /// Save module data - called on world save
        /// </summary>
        void SaveData();

        /// <summary>
        /// Cleanup module - called on mod unload
        /// </summary>
        void Close();
    }
}
