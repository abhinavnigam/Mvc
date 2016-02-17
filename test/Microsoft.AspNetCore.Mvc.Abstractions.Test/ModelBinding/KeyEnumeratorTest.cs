//using System.Collections.Generic;
//using Xunit;

//namespace Microsoft.AspNetCore.Mvc.ModelBinding
//{
//    public class KeyEnumeratorTest
//    {
//        [Fact]
//        public void KeyCollection_ReturnsEmptySequence_WhenRootNodeIsNotVisible()
//        {
//            // Arrange
//            var modelStateDictionary = new ModelStateDictionary();
//            modelStateDictionary.Root.IsVisible = false;

//            // Act
//            var keys = modelStateDictionary.Keys;

//            // Assert
//            Assert.Empty(keys);
//        }

//        [Fact]
//        public void KeyCollection_ReturnsRootNode_WhenVisible()
//        {
//            // Arrange
//            var modelStateDictionary = new ModelStateDictionary();
//            modelStateDictionary.Root.IsVisible = true;

//            // Act
//            var keys = modelStateDictionary.Keys;

//            // Assert
//            Assert.Equal(new[] { "" }, keys);
//        }

//        [Fact]
//        public void KeyCollection_ReturnsChildNodes()
//        {
//            // Arrange
//            var modelStateDictionary = new ModelStateDictionary();
//            modelStateDictionary.Root.Children = new List<ModelStateDictionary.ModelStateNode>
//            {
//                new ModelStateDictionary.ModelStateNode("Person")
//                {
//                    IsVisible = true,
//                    Children = new List<ModelStateDictionary.ModelStateNode>
//                    {
//                        new ModelStateDictionary.ModelStateNode("Person.Address")
//                        {
//                            IsVisible = false,
//                            Children = new List<ModelStateDictionary.ModelStateNode>
//                            {
//                                new ModelStateDictionary.ModelStateNode("Person.Address[0]") { IsVisible = true },
//                                new ModelStateDictionary.ModelStateNode("Person.Address[1]") { IsVisible = true },
//                            }
//                        }
//                    }
//                },
//                new ModelStateDictionary.ModelStateNode("CreditCard")
//                {
//                    Children = new List<ModelStateDictionary.ModelStateNode>
//                    {
//                        new ModelStateDictionary.ModelStateNode("CreditCard.Number") { IsVisible = true },
//                    }
//                },
//                new ModelStateDictionary.ModelStateNode("Cart")
//                {
//                    Children = new List<ModelStateDictionary.ModelStateNode>
//                    {
//                        new ModelStateDictionary.ModelStateNode("Cart[0].ProductId") { IsVisible = true },
//                        new ModelStateDictionary.ModelStateNode("Cart[1].ProductId") { IsVisible = false },
//                        new ModelStateDictionary.ModelStateNode("Cart[2].ProductId") { IsVisible = true },
//                    }
//                },
//            };

//            // Act
//            var keys = modelStateDictionary.Keys;

//            // Assert
//            Assert.Equal(new[] { "Person", "CreditCard.Number", "Cart[0].ProductId", "Cart[2].ProductId", "Person.Address[0]", "Person.Address[1]",  }, keys);
//        }
//    }
//}
