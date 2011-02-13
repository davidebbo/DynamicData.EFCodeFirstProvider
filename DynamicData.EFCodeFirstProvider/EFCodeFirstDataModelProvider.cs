using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Metadata.Edm;
using System.Data.Objects;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Web.DynamicData.ModelProviders;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;

namespace DynamicData.EFCodeFirstProvider {
    public class EFCodeFirstDataModelProvider : DataModelProvider {
        private ReadOnlyCollection<TableProvider> _tables;

        internal Dictionary<long, EFCodeFirstColumnProvider> RelationshipEndLookup { get; private set; }
        internal Dictionary<EntityType, EFCodeFirstTableProvider> TableEndLookup { get; private set; }
        private Func<object> ContextFactory { get; set; }
        private Dictionary<EdmType, Type> _entityTypeToClrType = new Dictionary<EdmType, Type>();
        private ObjectContext _objectContext;
        private ObjectItemCollection _objectSpaceItems;

        public EFCodeFirstDataModelProvider(Func<DbContext> contextFactory) {
            ContextFactory = () => {
                // Create the DbContext and get the underlying ObjectContext out of it
                return ((IObjectContextAdapter)contextFactory()).ObjectContext;
            };

            RelationshipEndLookup = new Dictionary<long, EFCodeFirstColumnProvider>();
            TableEndLookup = new Dictionary<EntityType, EFCodeFirstTableProvider>();

            DbContext dbContext = contextFactory();
            ContextType = dbContext.GetType();

            _objectContext = (ObjectContext)CreateContext();

            // get a "container" (a scope at the instance level)
            EntityContainer container = _objectContext.MetadataWorkspace.GetEntityContainer(_objectContext.DefaultContainerName, DataSpace.CSpace);
            // load object space metadata
            _objectContext.MetadataWorkspace.LoadFromAssembly(ContextType.Assembly);
            _objectSpaceItems = (ObjectItemCollection)_objectContext.MetadataWorkspace.GetItemCollection(DataSpace.OSpace);

            var tables = new List<TableProvider>();

            // Create a dictionary from entity type to entity set. The entity type should be at the root of any inheritance chain.
            IDictionary<EntityType, EntitySet> entitySetLookup = container.BaseEntitySets.OfType<EntitySet>().ToDictionary(e => e.ElementType);

            // Create a lookup from parent entity to entity
            ILookup<EntityType, EntityType> derivedTypesLookup = _objectContext.MetadataWorkspace.GetItems<EntityType>(DataSpace.CSpace).ToLookup(e => (EntityType)e.BaseType);

            // Keeps track of the current entity set being processed
            EntitySet currentEntitySet = null;

            // Do a DFS to get the inheritance hierarchy in order
            // i.e. Consider the hierarchy
            // null -> Person
            // Person -> Employee, Contact
            // Employee -> SalesPerson, Programmer
            // We'll walk the children in a depth first order -> Person, Employee, SalesPerson, Programmer, Contact.
            var objectStack = new Stack<EntityType>();
            // Start will null (the root of the hierarchy)
            objectStack.Push(null);
            while (objectStack.Any()) {
                EntityType entityType = objectStack.Pop();
                if (entityType != null) {
                    // Update the entity set when we are at another root type (a type without a base type).
                    if (entityType.BaseType == null) {
                        currentEntitySet = entitySetLookup[entityType];
                    }

                    // Ignore the special EdmMetadatas table, which is an implementation detail of EF Code First
                    if (currentEntitySet.Name != "EdmMetadatas") {
                        var table = CreateTableProvider(currentEntitySet, entityType);
                        tables.Add(table);
                    }
                }

                foreach (EntityType derivedEntityType in derivedTypesLookup[entityType]) {
                    // Push the derived entity types on the stack
                    objectStack.Push(derivedEntityType);
                }
            }

            _tables = tables.AsReadOnly();
        }

        public override object CreateContext() {
            return ContextFactory();
        }

        public override ReadOnlyCollection<TableProvider> Tables {
            get {
                return _tables;
            }
        }        

        internal Type GetClrType(EdmType entityType) {
            var result = _entityTypeToClrType[entityType];
            Debug.Assert(result != null, String.Format(CultureInfo.CurrentCulture, "Cannot map EdmType '{0}' to matching CLR Type", entityType));
            return result;
        }

        private Type GetClrType(EntityType entityType) {
            var objectSpaceType = (EntityType)_objectContext.MetadataWorkspace.GetObjectSpaceType(entityType);
            return _objectSpaceItems.GetClrType(objectSpaceType);
        }

        private TableProvider CreateTableProvider(EntitySet entitySet, EntityType entityType) {
            // Get the parent clr type
            Type parentClrType = null;
            EntityType parentEntityType = entityType.BaseType as EntityType;
            if (parentEntityType != null) {
                parentClrType = GetClrType(parentEntityType);
            }

            Type rootClrType = GetClrType(entitySet.ElementType);
            Type clrType = GetClrType(entityType);

            _entityTypeToClrType[entityType] = clrType;

            // Normally, use the entity set name as the table name
            string tableName = entitySet.Name;

            // But in inheritance scenarios where all types in the hierarchy share the same entity set,
            // we need to use the type name instead for the table name.
            if (parentClrType != null) {
                tableName = entityType.Name;
            }

            EFCodeFirstTableProvider table = new EFCodeFirstTableProvider(this, entitySet, entityType, clrType, parentClrType, rootClrType, tableName);
            TableEndLookup[entityType] = table;

            return table;
        }
    }
}
