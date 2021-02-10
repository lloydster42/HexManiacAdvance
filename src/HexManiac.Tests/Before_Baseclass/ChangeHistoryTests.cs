﻿using HavenSoft.HexManiac.Core;
using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.ViewModels;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HavenSoft.HexManiac.Tests {
   public class FakeChangeToken : List<int>, IChangeToken {
#pragma warning disable 0067 // it's ok if events are never used after implementing an interface
      public event EventHandler OnNewDataChange;
#pragma warning restore 0067
      public bool HasDataChange => Count > 0;
      public bool HasAnyChange => true;
      public FakeChangeToken() { }
      public FakeChangeToken(IEnumerable<int> data) : base(data) { }
   }

   public class ChangeHistoryTests : BaseViewModelTestClass {

      private readonly ChangeHistory<FakeChangeToken> history;
      private int callCount = 0;
      private List<int> recentChanges;

      public ChangeHistoryTests() {
         history = new ChangeHistory<FakeChangeToken>(changes => {
            callCount++;
            recentChanges = changes;
            return new FakeChangeToken(changes.Select(i => -i));
         });
      }

      [Fact]
      public void CannotUndoByDefault() {
         Assert.False(history.Undo.CanExecute(null));
      }

      [Fact]
      public void CallingUndoWhenStackIsEmptyDoesNothing() {
         history.Undo.Execute();
         history.Redo.Execute();
         Assert.Equal(0, callCount);
      }

      [Fact]
      public void CanUndoAfterCheckingCurrentTransaction() {
         history.CurrentChange.Count();
         Assert.True(history.Undo.CanExecute(null));
         Assert.False(history.Redo.CanExecute(null));
      }

      [Fact]
      public void UndoCallsRevertDelegate() {
         history.CurrentChange.Count();
         history.Undo.Execute();
         Assert.Equal(1, callCount);
         Assert.False(history.Undo.CanExecute(null));
         Assert.True(history.Redo.CanExecute(null));
      }

      [Fact]
      public void UndoPassesInRecentChanges() {
         history.CurrentChange.Add(3);
         history.Undo.Execute();

         Assert.NotNull(recentChanges);
         Assert.Single(recentChanges);
         Assert.Equal(3, recentChanges[0]);
      }

      [Fact]
      public void RedoPassesInRecentChanges() {
         history.CurrentChange.Add(3);
         history.Undo.Execute();
         history.Redo.Execute();

         Assert.NotNull(recentChanges);
         Assert.Single(recentChanges);
         Assert.Equal(-3, recentChanges[0]);
      }

      [Fact]
      public void CannotRedoAfterNewChange() {
         history.CurrentChange.Add(3);
         history.Undo.Execute();
         history.CurrentChange.Add(4);

         Assert.True(history.Undo.CanExecute(null));
         Assert.False(history.Redo.CanExecute(null));
      }

      [Fact]
      public void ClosingEmptyChangeDoesNotAddUndoItem() {
         history.ChangeCompleted();

         Assert.False(history.Undo.CanExecute(null));
      }

      [Fact]
      public void ClosingChangesAllowsForMultipleUndos() {
         history.CurrentChange.Add(3);
         history.ChangeCompleted();
         history.CurrentChange.Add(4);

         history.Undo.Execute();
         Assert.Equal(4, recentChanges[0]);

         history.Undo.Execute();
         Assert.Equal(3, recentChanges[0]);
      }

      [Fact]
      public void UndoCanExecuteChangeFiresCorrectly() {
         var canExecuteChangeCalled = 0;
         history.Undo.CanExecuteChanged += (sender, e) => canExecuteChangeCalled++;

         history.CurrentChange.Add(3);
         Assert.Equal(1, canExecuteChangeCalled);

         history.Undo.Execute();
         Assert.Equal(2, canExecuteChangeCalled);
      }

      [Fact]
      public void RedoCanExecuteChangeFiresCorrectlyForRedoOperation() {
         var canExecuteChangeCalled = 0;
         history.Redo.CanExecuteChanged += (sender, e) => canExecuteChangeCalled++;

         history.CurrentChange.Add(3);
         Assert.Equal(0, canExecuteChangeCalled);

         history.Undo.Execute();
         Assert.Equal(1, canExecuteChangeCalled);

         history.Redo.Execute();
         Assert.Equal(2, canExecuteChangeCalled);
      }

      [Fact]
      public void RedoCanExecuteChangeFiresCorrectlyForNewChange() {
         var canExecuteChangeCalled = 0;
         history.Redo.CanExecuteChanged += (sender, e) => canExecuteChangeCalled++;

         history.CurrentChange.Add(3);
         Assert.Equal(0, canExecuteChangeCalled);

         history.Undo.Execute();
         Assert.Equal(1, canExecuteChangeCalled);

         history.CurrentChange.Add(4); // adding another change clears the redo stack
         Assert.Equal(2, canExecuteChangeCalled);
      }

      [Fact]
      public void ThrowExceptionIfChangeStartsDuringUndo() {
         // setup a revert call that tries to access a change during revert
         ChangeHistory<FakeChangeToken> history = null;
         history = new ChangeHistory<FakeChangeToken>(token => history.CurrentChange);

         history.CurrentChange.ToString(); // create current change
         Assert.Throws<InvalidOperationException>(() => history.Undo.Execute());
      }

      [Fact]
      public void ThrowExceptionIfChangeCompletedDuringUndo() {
         ChangeHistory<FakeChangeToken> history = null;
         history = new ChangeHistory<FakeChangeToken>(token => {
            history.CurrentChange.ToString();
            history.ChangeCompleted();
            return new FakeChangeToken();
         });

         history.CurrentChange.ToString(); // create current change
         Assert.Throws<InvalidOperationException>(() => history.Undo.Execute());
      }

      [Fact]
      public void IsSavedIsRecalledCorrectlyWhenSavingAfterUndo() {
         history.CurrentChange.Add(1); // 1
         history.ChangeCompleted();
         history.CurrentChange.Add(2); // 1 2
         history.ChangeCompleted();
         history.CurrentChange.Add(3); // 1 2 3
         history.ChangeCompleted();

         history.Undo.Execute();       // 1 2  |  3
         history.Undo.Execute();       // 1  |  2 3
         history.TagAsSaved();

         history.Redo.Execute();       // 1 2  |  3
         history.CurrentChange.Add(4); // 1 2 4
         history.ChangeCompleted();

         history.Undo.Execute(); // 1 2  |  4
         history.Undo.Execute(); // 1  | 2 4

         // even though the redo history is different,
         // the current state is the same as when we last saved
         Assert.True(history.IsSaved);
      }

      [Fact]
      public void CanUndoFormatChange() {
         SetFullModel(0xFF);
         var viewPort = ViewPort;

         viewPort.SelectionStart = new Point(4, 0);
         viewPort.Edit("<000030>");
         Assert.Equal(0x04, Model.GetNextRun(0).Start);

         viewPort.Undo.Execute();
         Assert.Equal(int.MaxValue, Model.GetNextRun(0).Start);
      }

      [Fact]
      public void CanUndoDataMove() {
         SetFullModel(0xFF);
         var viewPort = ViewPort;

         viewPort.SelectionStart = new Point(8, 0);
         viewPort.Edit("<000030>");

         viewPort.SelectionStart = new Point(0, 0);
         viewPort.Edit("^bob\"\" \"Hello World!\"");

         viewPort.Undo.Execute(); // should undo the entire last edit transaction

         Assert.Equal(0xFF, Model[0]);
         Assert.Equal(8, Model.GetNextRun(0).Start);
         Assert.Equal(0x30, Model.GetNextRun(0x10).Start);
         Assert.Equal(int.MaxValue, Model.GetNextRun(0x31).Start);
      }

      [Fact]
      public void CanUndoFromToolChange() {
         SetFullModel(0xFF);
         var viewPort = ViewPort;

         viewPort.Edit("^bob\"\" ");
         var bobRun = Model.GetNextRun(0);
         Assert.Equal(1, bobRun.Length);

         // move the selection to force a break in the undo history
         viewPort.SelectionStart = new Point(3, 3);
         viewPort.SelectionStart = new Point(0, 0);

         viewPort.Tools.StringTool.Address = 0;
         viewPort.Tools.StringTool.Content = "Hello World!";
         viewPort.Undo.Execute(); // should undo only the tool change, not the name change

         Assert.Equal(0xFF, Model[0]);
         Assert.True(viewPort.Undo.CanExecute(null));
      }

      [Fact]
      public void UndoCanHandleNameMove() {
         SetFullModel(0xFF);
         var (model, viewPort) = (Model, ViewPort);

         // operation 1
         viewPort.Edit("<bob> 03 08 24 16 <bob>");

         // operation 2
         viewPort.SelectionStart = new Point(0, 1);
         viewPort.Edit("^bob ");

         Assert.Equal(0x10, model.ReadPointer(0x00));
         Assert.Equal("bob", ((Pointer)viewPort[0, 0].Format).DestinationName);

         // undo operation 2
         viewPort.Undo.Execute();
         Assert.Equal(Pointer.NULL, model.ReadPointer(0x00));
         Assert.Equal("bob", ((Pointer)viewPort[0, 0].Format).DestinationName);

         // operation 3
         viewPort.SelectionStart = new Point(0, 2);
         viewPort.Edit("^bob ");

         Assert.Equal(0x20, model.ReadPointer(0x00));
         Assert.Equal("bob", ((Pointer)viewPort[0, 0].Format).DestinationName);

         // undo operation 3
         viewPort.Undo.Execute();
         Assert.Equal(Pointer.NULL, model.ReadPointer(0x00));
         Assert.Equal("bob", ((Pointer)viewPort[0, 0].Format).DestinationName);
      }

      [Fact]
      public void UndoWorksAfterMidPointerEdit() {
         SetFullModel(0xFF);
         var (model, viewPort) = (Model, ViewPort);

         viewPort.Edit("<000100>");
         viewPort.SelectionStart = new Point(1, 0);
         viewPort.Edit("<000180>");
         viewPort.Undo.Execute();

         Assert.Equal(0x000100, model.ReadPointer(0));
      }

      [Fact]
      public void UndoRedoRestoresUnmappedNames() {
         SetFullModel(0xFF);
         var viewPort = ViewPort;

         viewPort.Edit("<bob>");
         viewPort.SelectionStart = new Point(0, 0);
         viewPort.Edit("<tom>");

         viewPort.Undo.Execute();
         Assert.Equal("bob", ((Pointer)viewPort[0, 0].Format).DestinationName);

         viewPort.Redo.Execute();
         Assert.Equal("tom", ((Pointer)viewPort[0, 0].Format).DestinationName);
      }

      [Fact]
      public void CanOverrideChangeCompletion() {
         // arrange: change, then "don't" complete, then change
         history.CurrentChange.Add(1);
         using (history.ContinueCurrentTransaction()) history.ChangeCompleted();
         history.CurrentChange.Add(2);

         // act: undo
         history.Undo.Execute();

         // assert: that was the only thing in the undo stack
         Assert.False(history.Undo.CanExecute(null));
      }
   }
}
