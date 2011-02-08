using System;
using System.Data.Metadata.Edm;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.DynamicData.ModelProviders;

namespace DynamicData.EFCodeFirstProvider {
    internal sealed class EFCodeFirstColumnProvider : ColumnProvider {
        private EFCodeFirstTableProvider _table;
        private EFCodeFirstAssociationProvider _association;
        private bool _isAssociation;
        private bool _isSortableProcessed;
        private const string StoreGeneratedMetadata = "http://schemas.microsoft.com/ado/2009/02/edm/annotation:StoreGeneratedPattern";

        public EFCodeFirstColumnProvider(EntityType entityType, EFCodeFirstTableProvider table, EdmMember m, bool isPrimaryKey)
            : base(table) {
            EdmMember = m;
            IsPrimaryKey = isPrimaryKey;
            _table = table;
            MaxLength = 0;
            Name = EdmMember.Name;
            // REVIEW Seems like extra properties added in partial classes are not even detected by the metadata engine
            IsCustomProperty = false;

            // REVIEW: This should probably be a debug assert or we should only pass an EmdProperty
            var property = EdmMember as EdmProperty;

            if (property != null) {
                IsForeignKeyComponent = DetermineIsForeignKeyComponent(property);
                IsGenerated = IsServerGenerated(property);
            }

            ProcessFacets();

            var navProp = m as NavigationProperty;
            if (navProp != null) {
                _isAssociation = true;
                long key = EFCodeFirstAssociationProvider.BuildRelationshipKey(entityType, navProp.FromEndMember);
                ((EFCodeFirstDataModelProvider)table.DataModel).RelationshipEndLookup[key] = this;
            }
        }

        private bool DetermineIsForeignKeyComponent(EdmProperty property) {
            var navigationProperties = property.DeclaringType.Members.OfType<NavigationProperty>();

            // Look at all NavigationProperties (i.e. strongly-type relationship columns) of the table this column belong to and
            // see if there is a foreign key that matches this property
            // If this is a 1 to 0..1 relationship and we are processing the more primary side. i.e in the Student in Student-StudentDetail
            // and this is the primary key we don't want to check the relationship type since if there are no constraints we will treat the primary key as a foreign key.
            return navigationProperties.Any(n => EFCodeFirstAssociationProvider.GetDependentPropertyNames(n, !IsPrimaryKey /* checkRelationshipType */).Contains(property.Name));
        }

        private static bool IsServerGenerated(EdmProperty property) {
            MetadataProperty generated;
            if (property.MetadataProperties.TryGetValue(StoreGeneratedMetadata, false, out generated)) {
                return "Identity" == (string)generated.Value || "Computed" == (string)generated.Value;
            }
            return false;
        }

        private void ProcessFacets() {
            foreach (Facet facet in EdmMember.TypeUsage.Facets) {
                switch (facet.Name) {
                    case "MaxLength":
                        if (facet.IsUnbounded) {
                            // If it's marked as unbounded, treat it as max int
                            MaxLength = Int32.MaxValue;
                        }
                        else if (facet.Value != null && facet.Value is int) {
                            MaxLength = (int)facet.Value;
                        }
                        break;
                    case "Nullable":
                        Nullable = (bool)facet.Value;
                        break;
                }
            }
        }

        internal EdmMember EdmMember {
            get;
            private set;
        }

        #region IEntityMember Members

        public override PropertyInfo EntityTypeProperty {
            // TODO implement
            get { return _table.EntityType.GetProperty(Name); }
        }

        public override Type ColumnType {
            get {
                if (base.ColumnType == null) {
                    // TODO some things might also be ComplexType. This apparently is meant for structs like Address
                    // that contains a number of related fields. The EFDS is supposed to surfaces all those properties as
                    // seperate columns.
                    var edmType = EdmMember.TypeUsage.EdmType;
                    if (edmType is EntityType) {
                        base.ColumnType = ((EFCodeFirstDataModelProvider)this.Table.DataModel).GetClrType(edmType);
                    }
                    else if (edmType is CollectionType) {
                        // get the EdmType that this CollectionType is wrapping
                        base.ColumnType = ((EFCodeFirstDataModelProvider)this.Table.DataModel).GetClrType(((CollectionType)edmType).TypeUsage.EdmType);
                    }
                    else if (edmType is PrimitiveType) {
                        base.ColumnType = ((PrimitiveType)edmType).ClrEquivalentType;
                    }
                    else {
                        Debug.Assert(false, String.Format(CultureInfo.CurrentCulture, "Unknown EdmType {0}.", edmType.GetType().FullName));
                    }
                }
                return base.ColumnType;
            }
        }

        public override bool IsSortable {
            get {
                if (!_isSortableProcessed) {
                    base.IsSortable = (ColumnType != typeof(byte[]));
                    _isSortableProcessed = true;
                }
                return base.IsSortable;
            }
        }

        public override AssociationProvider Association {
            get {
                if (!_isAssociation) {
                    return null;
                }

                if (_association == null) {
                    _association = new EFCodeFirstAssociationProvider(this, (NavigationProperty)EdmMember);
                }
                return _association;
            }
        }

        #endregion

        internal static bool IsSupportedEdmMemberType(EdmMember member) {
            var edmType = member.TypeUsage.EdmType;
            return edmType is EntityType || edmType is CollectionType || edmType is PrimitiveType;
        }
    }
}
