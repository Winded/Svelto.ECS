﻿using System;
using System.Collections.Generic;
using Svelto.ECS.Internal;

#if ENGINE_PROFILER_ENABLED && UNITY_EDITOR
using Svelto.ECS.Profiler;
#endif

namespace Svelto.ECS
{
    public partial class EnginesRoot : IDisposable
    {
        /// <summary>
        /// Dispose an EngineRoot once not used anymore, so that all the
        /// engines are notified with the entities removed.
        /// It's a clean up process.
        /// </summary>
        public void Dispose()
        {
            foreach (var groups in _groupEntityDB)
                foreach (var entityList in groups.Value)
                    entityList.Value.RemoveEntitiesFromEngines(_entityEngines);
        }

        ///--------------------------------------------

        public IEntityFactory GenerateEntityFactory()
        {
            return new GenericEntityFactory(new DataStructures.WeakReference<EnginesRoot>(this));
        }

        public IEntityFunctions GenerateEntityFunctions()
        {
            return new GenericEntityFunctions(new DataStructures.WeakReference<EnginesRoot>(this));
        }

        ///--------------------------------------------

        EntityStructInitializer BuildEntity<T>(EGID entityID, object[] implementors)
            where T : IEntityDescriptor, new()
        {
            var dic = EntityFactory.BuildGroupedEntityViews(entityID,
                              _groupedEntityToAdd.current,
                              EntityDescriptorTemplate<T>.descriptor.entitiesToBuild,
                              implementors);
            
            return new EntityStructInitializer(entityID, dic);
        }

        EntityStructInitializer BuildEntity(EGID entityID, 
                                IEntityBuilder[] entityToBuild,
                                object[] implementors)
        {
            var dic = EntityFactory.BuildGroupedEntityViews(entityID,
                                                  _groupedEntityToAdd.current,
                                                  entityToBuild,
                                                   implementors);
            
            return new EntityStructInitializer(entityID, dic);
        }

        ///--------------------------------------------

        void Preallocate<T>(int groupID, int size) where T : IEntityDescriptor, new()
        {
            var entityViewsToBuild = EntityDescriptorTemplate<T>.descriptor.entitiesToBuild;
            var count              = entityViewsToBuild.Length;

            //reserve space in the database
            Dictionary<Type, ITypeSafeDictionary> @group;
            if (_groupEntityDB.TryGetValue(groupID, out group) == false)
                group = _groupEntityDB[groupID] = new Dictionary<Type, ITypeSafeDictionary>();

            //reserve space in building buffer
            Dictionary<Type, ITypeSafeDictionary> @groupBuffer;
            if (_groupedEntityToAdd.current.TryGetValue(groupID, out @groupBuffer) == false)
                @groupBuffer = _groupedEntityToAdd.current[groupID] = new Dictionary<Type, ITypeSafeDictionary>();

            ITypeSafeDictionary dbList;

            for (var index = 0; index < count; index++)
            {
                var entityViewBuilder = entityViewsToBuild[index];
                var entityViewType    = entityViewBuilder.GetEntityType();

                if (group.TryGetValue(entityViewType, out dbList) == false)
                    group[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.AddCapacity(size);
                
                if (@groupBuffer.TryGetValue(entityViewType, out dbList) == false)
                    @groupBuffer[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.AddCapacity(size);
            }

            if (group.TryGetValue(_typeEntityInfoView, out dbList) == false)
                group[_typeEntityInfoView] = EntityBuilder<EntityInfoView>.Preallocate(ref dbList, size);
            else
                dbList.AddCapacity(size);

            if (@groupBuffer.TryGetValue(_typeEntityInfoView, out dbList) == false)
                @groupBuffer[_typeEntityInfoView] = EntityBuilder<EntityInfoView>.Preallocate(ref dbList, size);
            else
                dbList.AddCapacity(size);
        }
        
        ///--------------------------------------------
        /// 
        void RemoveEntity(EGID entityGID)
        {
            var entityViewInfoDictionary = _groupEntityDB[entityGID.groupID][_typeEntityInfoView] as TypeSafeDictionary<EntityInfoView>;
            var entityInfoView = entityViewInfoDictionary[entityGID.entityID];
            var entityBuilders = entityInfoView.entityToBuild;
            var entityBuildersCount = entityBuilders.Length;
            
            //for each entity view generated by the entity descriptor
            for (var i = 0; i < entityBuildersCount; i++)
            {
                var entityType = entityBuilders[i].GetEntityType();
                var group = _groupEntityDB[entityGID.groupID];

                var groupedEntities = _groupEntityDB[entityGID.groupID];
                var typeSafeDictionary = groupedEntities[entityType];
                
                typeSafeDictionary.RemoveEntityFromEngines(entityGID, _entityEngines);
                
                RemoveEntityFromGroup(group, entityType, entityGID);
            }
        }

        static void RemoveEntityFromGroup(Dictionary<Type, ITypeSafeDictionary> @group, Type entityType, EGID id)
        {
            var typeSafeList = @group[entityType];
            if (typeSafeList.Remove(id.entityID) == false) //clean up
                @group.Remove(entityType);
        }

        void RemoveGroupAndEntitiesFromDB(int groupID)
        {
            foreach (var entiTypeSafeList in _groupEntityDB[groupID])
                entiTypeSafeList.Value.RemoveEntitiesFromEngines(_entityEngines);

            _groupEntityDB.Remove(groupID);
        }

        ///--------------------------------------------

        void SwapEntityGroup(int entityID, int fromGroupID, int toGroupID)
        {
            DBC.ECS.Check.Require(fromGroupID != toGroupID,
                          "can't move an entity to the same group where it already belongs to");

            var entityegid = new EGID(entityID, fromGroupID);
            var groupedEntities = _groupEntityDB[fromGroupID];
            var entityInfoViewDictionary = (TypeSafeDictionary<EntityInfoView>) groupedEntities[_typeEntityInfoView];
            var entityViewBuilders = entityInfoViewDictionary[entityegid.entityID].entityToBuild;
            var entityViewBuildersCount = entityViewBuilders.Length;

            Dictionary<Type, ITypeSafeDictionary> groupedEntityViewsTyped;
            if (_groupEntityDB.TryGetValue(toGroupID, out groupedEntityViewsTyped) == false)
            {
                groupedEntityViewsTyped = new Dictionary<Type, ITypeSafeDictionary>();

                _groupEntityDB.Add(toGroupID, groupedEntityViewsTyped);
            }
            
            ITypeSafeDictionary toSafeDic;

            for (var i = 0; i < entityViewBuildersCount; i++)
            {
                var entityViewBuilder = entityViewBuilders[i];
                var entityViewType    = entityViewBuilder.GetEntityType();

                var fromSafeDic = groupedEntities[entityViewType];
                if (groupedEntityViewsTyped.TryGetValue(entityViewType, out toSafeDic) == false)
                    groupedEntityViewsTyped[entityViewType] = toSafeDic = fromSafeDic.Create();

                entityViewBuilder.MoveEntityView(entityegid, toGroupID, fromSafeDic, toSafeDic);
            }
            
            if (groupedEntityViewsTyped.TryGetValue(_typeEntityInfoView, out toSafeDic) == false)
                groupedEntityViewsTyped[_typeEntityInfoView] = toSafeDic = entityInfoViewDictionary.Create();

            EntityBuilder<EntityInfoView>.MoveEntityView(entityegid, toGroupID, entityInfoViewDictionary, toSafeDic);
        }
        
        EGID SwapFirstEntityGroup(int fromGroupID, int toGroupId)
        {
            var firstID =
                ((TypeSafeDictionary<EntityInfoView>) _groupEntityDB[fromGroupID][_typeEntityInfoView]).FasterValues[0].ID.entityID;
            
            SwapEntityGroup(firstID, fromGroupID, toGroupId);
            
            return new EGID(firstID, toGroupId);
        }

        readonly entitiesDB _DB;
        
        //grouped set of entity views, this is the standard way to handle entity views
        readonly Dictionary<int, Dictionary<Type, ITypeSafeDictionary>>         _groupEntityDB;
        static readonly Type _typeEntityInfoView = typeof(EntityInfoView);
    }

    public struct EntityStructInitializer
    {
        public EntityStructInitializer(EGID id, Dictionary<Type, ITypeSafeDictionary> current)
        {
            _current = current;
            _id = id;
        }

        public void Init<T>(T initializer) where T: struct, IEntityStruct
        {
            var typeSafeDictionary = (TypeSafeDictionary<T>) _current[typeof(T)];

            initializer.ID = _id;
            
            typeSafeDictionary.GetFasterValuesBuffer()[typeSafeDictionary.FindElementIndex(_id.entityID)] = initializer;
        }

        readonly Dictionary<Type, ITypeSafeDictionary> _current;
        readonly EGID _id;
    }
}